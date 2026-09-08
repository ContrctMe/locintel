"""Guard against silently excluding application async bodies from coverage."""

from pathlib import Path
import sys
import xml.etree.ElementTree as ET

required = {
    "LocIntel.Modules.Tenancy.Sites.SiteEndpoints": {"Create", "List", "Update"},
    "LocIntel.Modules.Identity.Users.OrganizationUpsertedHandler": {"Handle"},
    "LocIntel.Modules.Ingest.StagingService": {"StageAsync"},
}
found = set()
for report in Path(sys.argv[1]).rglob("coverage.cobertura.xml"):
    for cls in ET.parse(report).iter("class"):
        name = cls.get("name", "")
        for owner, methods in required.items():
            for method in methods:
                if name.startswith(owner + "/<" + method + ">") and cls.findall("lines/line"):
                    found.add((owner, method))
                if name == owner and cls.findall(f"methods/method[@name='{method}']/lines/line"):
                    found.add((owner, method))
missing = {(owner, method) for owner, methods in required.items() for method in methods} - found
if missing:
    sys.exit("Async coverage bodies missing: " + ", ".join(f"{o}.{m}" for o, m in sorted(missing)))
print("Async coverage denominator verified: endpoint, projection handler, and staging bodies")
