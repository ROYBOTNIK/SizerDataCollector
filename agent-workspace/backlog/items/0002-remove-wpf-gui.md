# Remove WPF GUI

Status: done
Priority: P1
Source: inbox note `Remove-WPF-GUI.md`
Goal: remove the WPF GUI from the production app and ensure no references to removed GUI code remain.

## User Note

> The WPF UI was a mistake - it created too much friction when trying to get it to work
>
> go ahead and remove it from the production version of the app and any supporting code.
>
> make sure that the there is no references to removed code left over - if there is any non GUI related that will be affected - assess the correct pro

## Evidence

- `SizerDataCollector.GUI.WPF/` does not exist on `codex/production-workspace`.
- `SizerDataCollector.sln` has no GUI/WPF project.
- `OptiFresh.OeeSuite.sln` has no GUI/WPF project.
- Search for removed GUI symbols only returned this backlog item/inbox text plus unrelated `GUID` wording.

## Acceptance Checks

- No production DB or service touched.
- No code change needed; existing branch already removed the WPF project.

Protected action: no
Decision: do nothing won because the requested removal is already satisfied.
