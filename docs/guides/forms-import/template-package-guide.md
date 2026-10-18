# Template package guide

**Marketplace** lists template packages: Automotive (APQP, PPAP incl. PFMEA, control plan, run at rate, launch readiness), Aerospace (FAI, configuration control, process verification), Medical device (DHF, DMR, DHR, validation, CAPA), Manufacturing (NCR, CAPA, audits, calibration, supplier quality) and Electronics (installable, no templates yet – see Questions).

- **Install** (`templates.install`) – templates arrive as **draft** templates at the chosen site and go through normal approval.
- **Export** (`templates.export`) – selected templates are written to a package file (`.qgt` content with a manifest and content hash).
- **Import** – a package file is validated and installed as drafts.

Compliance mapping content in packs is marked Unverified until verified by someone with `compliance.verify`.
