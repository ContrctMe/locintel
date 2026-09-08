#!/usr/bin/env python3
"""Run with python3 tests/reporting-image.test.py; no containers required."""
import importlib.util
import io
import pathlib
import sys
import urllib.error
from unittest.mock import Mock, patch

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location(
    "reporting_image", pathlib.Path(__file__).parents[1] / "tools/reporting-image.py"
)
harness = importlib.util.module_from_spec(spec)
spec.loader.exec_module(harness)
harness.s3_port = "19000"
harness.s3_tls = object()

for artifact, expected_path in [
    ({"id": "pdf-artifact", "fileId": "published-file", "contentType": "application/pdf"},
     "/api/files/published-file/download"),
    ({"id": "bundle", "fileId": None, "contentType": "application/zip"},
     "/api/reports/run/artifacts/bundle/download"),
]:
    with patch.object(harness, "request", return_value={"url": "https://report-s3:9000/reports/object?signature=valid"}) as request, \
         patch.object(harness.urllib.request, "urlopen", return_value=io.BytesIO(b"artifact bytes")) as open_url:
        assert harness.download("run", artifact) == b"artifact bytes"
        request.assert_called_once_with(expected_path)
        download_request = open_url.call_args.args[0]
        assert download_request.full_url == "https://127.0.0.1:19000/reports/object?signature=valid"
        assert download_request.get_header("Host") == "report-s3:9000"
        assert open_url.call_args.kwargs["context"] is harness.s3_tls

harness.base = "http://localhost"
harness.client = Mock()
harness.client.open.side_effect = urllib.error.HTTPError(
    "http://localhost/missing", 404, "Not Found", {}, io.BytesIO(b"missing artifact")
)
try:
    harness.request("/missing")
    raise AssertionError("A failed request must propagate")
except RuntimeError as error:
    assert str(error) == "GET /missing: 404 missing artifact", str(error)
    assert isinstance(error.__cause__, urllib.error.HTTPError)

print("Reporting image routes, signed download transport, and GET error reporting passed.")
