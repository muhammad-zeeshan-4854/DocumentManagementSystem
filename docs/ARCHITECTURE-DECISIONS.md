# Architecture Decisions

The main choices in this project and why I made them.

---

## 1. Business rules live in stored procedures

Every rule is checked in an SP: title required, allowed file types and sizes, company check, soft delete, note length, paging limits and so on. The C# side has no validation attributes. It passes the data to the SP and shows the message that comes back.

**Why:** the rules are in one place. Any other caller (a job, an import script, an API) gets the same behaviour, and things like the allowed file types can change with data instead of a deployment.

**Browser checks:** the upload form and the note editor do quick checks in JavaScript first (title or note text empty, no file, file type not allowed, empty file, file too large), using the same limits and messages as the SPs. This only saves a round trip for obvious mistakes. 

### How errors come back

| SP throws | Meaning | HTTP |
|---|---|---|
| 50400 | Invalid input | 400 |
| 50404 | Not found | 404 |
| 50409 | Already done / conflict | 409 |
| anything else | System error | 500 |

Every SP uses the same TRY/CATCH: roll back, write system errors to `dbo.ErrorLog`, then throw. In C#, `SqlHelper` is the only place that handles `SqlException`. It turns 504xx into `BusinessRuleException` (the message is safe to show) and anything else into `DataAccessException`. `GlobalExceptionHandler` logs each error once, and the user sees an error id that matches the log lines.

---

## 2. The file path never leaves the server

The `Document` table from the spec has no file column, so file details go in a separate 1:1 table, `dbo.DocumentFile` (original name, type, size, `FilePath`, `StoredFileName`).

- The SP decides the folder (`{CompanyID}/{yyyy}/{MM}`) and a GUID file name. Nothing comes from the user.
- The root folder is `dbo.Parameters.FileBasePath`, outside `wwwroot`.
- The browser only knows the document ID. View and download call `dbo.GetDocumentFileByID`, which checks the company and that the document isn't deleted, then returns the location to the server.
- `FileUtility` also refuses paths that are rooted or contain `..`.

**Upload order:** the SP runs first, so a rejected upload never touches the disk. Then the file is saved. If saving fails, `DeleteFailedUploadDocument` removes the row, so the list never shows a document without a file.

**File checks:** C# reads facts about the file (MIME type, first bytes, damaged?, password?, scripts or macros?) and passes them to `InsertDocument`, which decides. There is one checker class per file family (`PdfFileChecker`, `OfficeFileChecker`, `ImageFileChecker`, `TextFileChecker`), all based on `FileContentChecker`. A new type means a new class and one DI line.

---

## 3. Paging is done in the SP

`GetDocumentList` and `GetAuditLogList` take `@PageNumber` and `@PageSize` and use `OFFSET / FETCH`, so only one page of rows leaves SQL Server. `COUNT(*) OVER ()` returns the total on every row, so one call gives both the page and the total.

The document list uses a filtered index:

```sql
IX_Document_CompanyId_DateAdded (CompanyId, DateAdded DESC, Id DESC) INCLUDE (Title) WHERE DeletedDate IS NULL
```

It covers the company filter, the title search and the sort. Deleted rows are not in it, so the list stays fast as deleted rows build up.

---

## 4. Settings in `dbo.Parameters`

Things that may change while the site runs are in one row of `dbo.Parameters`: title, file folder, upload size, page sizes, text lengths and default company. `CHECK` constraints keep the values valid.

The SPs read the limits themselves, so a rule and its number stay together. The app caches the row for 60 seconds. If the database is down, it uses built-in defaults for a short time, so pages and the error page still render.

Only start-up items stay in `appsettings.json`: connection string, timeouts, logging and cache time.

---

## 5. Company isolation in SQL

Companies are rows in `dbo.Company`. Every SP that takes `@CompanyID` checks that the company is active and filters by it. Another company's document returns 404, not 403, so its ID reveals nothing.

There is no sign-in yet, so the company comes from a dropdown (kept in a cookie) through `ICompanyContext`. With sign-in, a new `ICompanyContext` that reads the user's company would replace it and nothing else would change. Until then, the dropdown is not security.

---

## 6. Audit log written by the SPs

`dbo.AuditLog` records uploads, failed uploads, views, downloads, deletes and notes. The SP that does the work calls `InsertAuditLog` inside its own transaction, so there is never a change without its audit row, and no C# path can forget it. A refused action throws before the audit call, so it isn't recorded.

`DocumentId` and `NoteId` have no foreign key, so the history stays even after a failed upload's row is removed. The audit page shows the document title next to each row.

---

## Smaller choices

- **Dapper, not Entity Framework.** All logic is in SPs, so EF adds nothing.
- **Soft delete.** `DeletedDate` is set; the row, file and notes are kept.
- **Notes over AJAX**, with positions saved as percentages of the preview, so pins stay in place when the window resizes. Note text is shown with `textContent` only.
- **Word preview.** Browsers can't show `.docx`, so `WordPreviewBuilder` makes a simple HTML copy (text, headings, lists, tables, pictures) without any extra library.
- **Every form post redirects** (Post/Redirect/Get). Pages are sent with `no-store`, so without this the Back button would ask to resubmit a form.
- **SQL injection:** only stored procedures with typed Dapper parameters, and no dynamic SQL inside the SPs.
- **Naming:** SPs are verb + entity (`GetDocumentList`, `GetDocumentByID`, `DeleteDocumentByID`); C# uses Hungarian notation (`iDocumentID`, `strTitle`).
