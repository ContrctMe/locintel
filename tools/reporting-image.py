#!/usr/bin/env python3
"""HTTP assertions for reporting-image.sh; only fixture login runs in Development."""
import http.cookiejar
import io
import json
import pathlib
import ssl
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile


def request(path, body=None, method=None):
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(base + path, data=data, method=method,
                                 headers={"Origin": base, "Content-Type": "application/json"})
    try:
        with client.open(req, timeout=30) as response:
            payload = response.read()
            return json.loads(payload) if payload else None
    except urllib.error.HTTPError as error:
        raise RuntimeError(f"{req.get_method()} {path}: {error.code} {error.read().decode()}") from error


def download(run_id, artifact):
    if artifact["contentType"] == "application/pdf":
        assert artifact["fileId"], "PDF has not been published to Storage"
        path = f"/api/files/{artifact['fileId']}/download"
    else:
        assert artifact["contentType"] == "application/zip", artifact
        path = f"/api/reports/{run_id}/artifacts/{artifact['id']}/download"
    ticket = request(path)
    url = urllib.parse.urlsplit(ticket["url"])
    assert url.hostname == "report-s3", url.hostname
    assert url.scheme == "https", url.scheme
    # The host runner reaches the container through its published loopback port.
    # Keep the signed Host header intact, so MinIO validates the real S3 signature.
    local = urllib.parse.urlunsplit((url.scheme, f"127.0.0.1:{s3_port}", url.path, url.query, ""))
    with urllib.request.urlopen(urllib.request.Request(local, headers={"Host": url.netloc}), timeout=30, context=s3_tls) as response:
        return response.read()


def main():
    global base, s3_port, s3_tls, client
    mode, base, s3_port, output_arg, s3_ca = sys.argv[1:]
    output = pathlib.Path(output_arg)
    s3_tls = ssl.create_default_context(cafile=s3_ca)
    cookies = http.cookiejar.LWPCookieJar(str(output / "session.cookies"))
    client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cookies))

    if mode == "prepare":
        request("/auth/login?returnUrl=%2Fme&hint=operator%40locintel.local")
        operator = client
        alice_cookies = http.cookiejar.LWPCookieJar(str(output / "session.cookies"))
        client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(alice_cookies))
        me = request("/auth/login?returnUrl=%2Fme&hint=alice%40acme.test")
        alice = client
        client = operator
        request(f"/api/operator/orgs/{me['activeOrg']}/entitlements/sites.max", {"value": "100000"}, "PUT")
        client = alice
        root = next(n["id"] for n in request("/api/hierarchy")["nodes"] if n["depth"] == 0)
        sites = [request("/api/sites", {"name": f"Image qualification {i:03}", "nodeId": root,
                                      "timeZone": "America/Chicago", "latitude": 41.88, "longitude": -87.63})["id"]
                 for i in range(100)]
        (output / "sites.json").write_text(json.dumps(sites))
        alice_cookies.save(ignore_discard=True)
        print("Prepared 100 sites and an authenticated fixture session; no reports submitted.", flush=True)
    elif mode == "verify":
        cookies.load(ignore_discard=True)
        sites = json.loads((output / "sites.json").read_text())
        before = request("/api/reports/quota")
        metrics = []
        for report_type, report_mode, selected, count in [
            ("site", "single", sites[:1], 1),
            ("sites-summary", "aggregate", sites, 1),
            ("site", "bulk", sites, 100),
        ]:
            started = time.monotonic()
            run_id = request("/api/reports", {"reportType": report_type, "mode": report_mode,
                                            "selection": "selected", "siteIds": selected, "options": {}})["id"]
            deadline = time.monotonic() + 300
            while True:
                job = request(f"/api/reports/{run_id}")
                assert job["state"] not in ("Failed", "CompletedWithErrors", "Canceled", "Expired"), job
                if job["state"] == "Completed" and all(a["fileId"] for a in job["artifacts"] if a["contentType"] == "application/pdf"):
                    break
                assert time.monotonic() < deadline, f"Timed out: {run_id}"
                time.sleep(0.5)
            ready_ms = round((time.monotonic() - started) * 1000)
            pdfs = [a for a in job["artifacts"] if a["contentType"] == "application/pdf"]
            assert len(pdfs) == count, job
            assert len(job["items"]) == count and all(i["state"] == "Succeeded" for i in job["items"]), job
            byte_count = 0
            if report_mode == "bulk":
                bundles = [a for a in job["artifacts"] if a["contentType"] == "application/zip"]
                assert len(bundles) == 1, job
                content = download(run_id, bundles[0])
                byte_count = len(content)
                (output / "bulk-100.zip").write_bytes(content)
                with zipfile.ZipFile(io.BytesIO(content)) as archive:
                    assert archive.testzip() is None
                    assert len(archive.namelist()) == 101
                    manifest = json.loads(archive.read("manifest.json"))
                    assert len(manifest["items"]) == 100
                    assert all(i["State"] == "Succeeded" for i in manifest["items"])
                    assert sorted(s for i in manifest["items"] for s in i["SiteIds"]) == sorted(sites)
                    assert all(archive.read(name).startswith(b"%PDF-") for name in archive.namelist() if name.endswith(".pdf"))
            else:
                content = download(run_id, pdfs[0])
                assert content.startswith(b"%PDF-")
                byte_count = len(content)
                (output / f"{report_mode}.pdf").write_bytes(content)
            for site in selected:
                listing = request(f"/api/files?siteId={site}&limit=200")
                linked = [f for f in listing["items"] if f.get("originId") == run_id]
                assert len(linked) == 1, (site, listing)
                assert linked[0]["id"] in [a["fileId"] for a in pdfs]
            (output / f"{report_mode}-run.json").write_text(json.dumps(job, indent=2))
            metrics.append({"mode": report_mode, "runId": run_id, "pdfCount": count,
                            "submissionToPublishedMilliseconds": ready_ms, "downloadBytes": byte_count})
            print(json.dumps(metrics[-1]), flush=True)
        after = request("/api/reports/quota")
        assert after["consumed"] - before["consumed"] == 102, (before, after)
        assert after["reserved"] == before["reserved"], (before, after)
        (output / "metrics.json").write_text(json.dumps({"runs": metrics, "quotaBefore": before, "quotaAfter": after}, indent=2))
    else:
        raise ValueError(mode)


if __name__ == "__main__":
    main()
