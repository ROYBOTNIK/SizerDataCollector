# Method

This workspace adapts three ideas:

- Interpretable Context Methodology / Model Workspace Protocol: folders are the orchestration surface. Each numbered stage has one job, a markdown contract, scoped inputs, and inspectable outputs.
- Karpathy autoresearch: keep the editable surface small, use a repeatable loop, measure each iteration, and keep or discard changes based on evidence.
- Nous autoreason: every review includes the unchanged incumbent as a valid option. A change only wins when it beats doing nothing against the rubric.

## Local Adaptation

Context layers:

| Layer | Local file/folder |
| --- | --- |
| 0 identity | `AGENTS.md` |
| 1 routing | `CONTEXT.md` |
| 2 stage contract | `stages/*/CONTEXT.md` |
| 3 reference | `_config/*.md`, `../MD-DOCS/*.md` |
| 4 working artifacts | `inbox/`, `backlog/items/`, `stages/*/output/` |

Approval stand-in:

- Compare `A = do nothing`, `B = minimal change`, and `AB = broader synthesis`.
- Prefer `A` when evidence is weak, tests are missing, or the change is speculative.
- Prefer `B` when it satisfies the item and keeps risk low.
- Prefer `AB` only when two related changes must ship together to avoid a broken state.

Sources:

- Local PDF: `C:\Users\RoydonAdlam\Documents\Service-Scope-Quoting-Workspace\workspace-inspiration\Interpretable_Context_Methdology_.pdf`
- Notes: `C:\Users\RoydonAdlam\Documents\Service-Scope-Quoting-Workspace\workspace-inspiration\links-to-auto-methods.md`
- Autoreason: https://github.com/NousResearch/autoreason
- Autoresearch: https://github.com/karpathy/autoresearch
