# ADR 0010: Enterprise forms, template library and import center

Status: accepted, 2026-10-17.

## Context
Existing pieces (inspected): form templates are `Document` rows with `Kind = FormTemplate`; schemas live on `DocumentRevision.FieldSchema`
and are validated by `Application/Forms/FormSchema.cs`; records are `FormRecord` bound to `TemplateRevisionId` (`Modules/Quality/FormService.cs`).
Document revisions and approvals use the shared workflow engine; the audit engine is `IAudited` + the audit interceptor; files use the
storage abstraction with an attachment scanning boundary; background jobs use Hangfire; row imports use `Modules/Apqp/TabularImport.cs`
(ClosedXML); PDFs use `Services/SimplePdf.cs` + `PdfCanvas`; permissions are in `Domain/Security/Permissions.cs` with site/department scope.

## Decision
- Keep document control as the owner of template revisions. The Enterprise Template Library adds catalog metadata, categories, packages
  and a normalized form definition linked to the document revision (one-to-one), so existing forms and records keep working unchanged.
- Form schema v2 is a superset of the current field list (a v1 list still parses). It adds sections, tables, layout and signature blocks.
- Tables live in the `public` schema with real foreign keys to documents, revisions, sites, departments, users and files. Approved revisions
  are immutable in code (content lock) and in the database (trigger).
- Imports run as background jobs with audited state transitions; originals are kept as stored files with SHA-256 hashes.
- The reference SQL package is not applied; its ideas are rebuilt code-first with our own indexes and constraints.

## Consequences
Old forms need no migration; an opt-in wizard moves a template to schema v2 as a new revision.
