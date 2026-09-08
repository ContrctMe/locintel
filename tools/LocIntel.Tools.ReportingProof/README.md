# PDF renderer qualification

Run from the repository root:

```bash
bash tools/reporting-proof.sh locintel:compat-dev /tmp/locintel-reporting-proof
```

The supplied image must have the production .NET 10 runtime. The runner publishes
the proof for Linux x64, then runs as a non-root user with no network, a read-only
root filesystem, one CPU and a 256 MiB memory limit. It does not rebuild the API
image or start application services. NuGet restore needs network on first use.

Outputs: a site PDF, 100-row aggregate PDF, ZIP with both and a warning manifest,
plus elapsed-time/peak-working-set metrics. These are a renderer check, not bulk
job throughput measurements. The map is a pre-rendered synthetic PNG; the JPEG is
a synthetic building illustration. No production map composition, fetching,
authorization, image sanitization or job orchestration is implemented here.

The executable reopens both PDFs to check pagination and the ZIP to check entries.
Run `python3 tools/LocIntel.Tools.ReportingProof/verify.py /tmp/locintel-reporting-proof`
with `pypdf` installed for text, embedded-font, image, complete-row and archive-byte
checks. This optional inspection dependency is not a production dependency.
For visual verification, render every page with `pdftoppm -png` and inspect them.
Use `pdftotext` to check Latin diacritics, complete table rows, and warning text;
use `pdffonts` to confirm font embedding. Byte-level PDF equality is not expected
because timestamps and document identifiers vary.

The Reporting module's `Rendering/Fonts/LiberationSans-*.ttf` are unmodified
Liberation fonts, embedded in its assembly and distributed with their SIL Open
Font License in `Rendering/Fonts/LICENSE_LIBERATION`. These copies came
from PDF.js's standard font assets. Fonts are resolved explicitly, never through
the host OS. The synthetic fixtures belong to this repository and can be rebuilt
with `python3 create-fixtures.py` (Pillow required only for fixture regeneration).

PDFsharp/MigraDoc 6.2.4 is MIT-licensed. Preserve its distributed license notice
when packaging or moving this dependency into the application. See the
[upstream license](https://docs.pdfsharp.net/General/License/License.html).

The proof uses production font resolution and image normalization (SkiaSharp
3.119.4, MIT). It still uses synthetic layouts and data, so it does not prove
the production definitions or authorization. See the
[reporting authoring guide](../../docs/reporting.md).
