## Agent skills

### Issue tracker

Issues and specs are GitHub issues on agabra23/wedding-site, managed with the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Default vocabulary: needs-triage, needs-info, ready-for-agent, ready-for-human, wontfix. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: `GLOSSARY.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.

## Styling

Colors and fonts are tokens in the `@theme` block of `src/index.css`. Components use only the semantic utilities (`bg-surface`, `text-ink`, `text-primary`, `font-display`, ...), never raw palette colors or hex values. Add or change tokens there instead of inlining values. Nav items live in the `NAV_ITEMS` array in `src/components/Layout.tsx`.

UI feel (transitions, icons, motion): see `docs/ui-design-rules.md`. Always smooth and clean, never bulky or rigid.
