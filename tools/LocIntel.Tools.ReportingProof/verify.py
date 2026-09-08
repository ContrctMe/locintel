"""Verify generated proof artifacts. Requires pypdf; rendering is checked separately."""
import json
from pathlib import Path
import sys
import zipfile

from pypdf import PdfReader

root = Path(sys.argv[1])
texts = {}
for name, expected_pages in [("site-reference.pdf", 3), ("aggregate-reference.pdf", 5)]:
    pdf = PdfReader(root / name)
    assert len(pdf.pages) == expected_pages, (name, len(pdf.pages))
    texts[name] = "\n".join(page.extract_text() for page in pdf.pages)
    for number, page in enumerate(pdf.pages, 1):
        text = page.extract_text()
        assert f"Page {number} of {expected_pages}" in text, (name, number, "footer")
        for font_ref in page["/Resources"]["/Font"].values():
            font = font_ref.get_object()
            faces = font.get("/DescendantFonts", [font])
            for face in faces:
                descriptor = face.get_object()["/FontDescriptor"]
                assert any(key in descriptor for key in ("/FontFile", "/FontFile2", "/FontFile3")), "Font must be embedded"
    assert sum(len(page.images) for page in pdf.pages) >= (2 if name.startswith("site") else 1)
    assert "Montréal" in texts[name] and "Québec" in texts[name]
    assert "Synthetic map fixture" in texts[name]
    assert "could not be rendered" not in texts[name].lower()

for i in range(1, 101):
    assert texts["aggregate-reference.pdf"].count(f"Site {i:03} -") == 1, i
for i in range(1, 25):
    assert texts["site-reference.pdf"].count(f"Observation {i:02}:") == 1, i
assert "[Photograph unavailable]" in texts["site-reference.pdf"]
assert "façade" in texts["site-reference.pdf"]
with zipfile.ZipFile(root / "reference-bundle.zip") as bundle:
    assert bundle.testzip() is None
    assert set(bundle.namelist()) == {*texts, "manifest.json"}
    for name in texts:
        assert bundle.read(name) == (root / name).read_bytes()
    manifest = json.loads(bundle.read("manifest.json"))
    assert set(manifest["files"]) == set(texts)
    assert len(manifest["warnings"]) == 1
print("PASS: eight pages, embedded fonts, Latin text, images, 100 rows, 24 observations, ZIP bytes and manifest")
