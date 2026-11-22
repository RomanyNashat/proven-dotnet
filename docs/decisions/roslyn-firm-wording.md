# Why the Roslyn rule in core.md is firm

Decided before 0.3.0; recorded here in 0.3.56, when the history left `core.md`.

- Worded as "optional, if connected", the Roslyn MCP got zero calls in 47 sessions while the same symbol
  questions were answered by grep.
- Over two weeks of real use, 692 symbol questions went through `grep` inside a Bash command, 1 through the
  Grep tool and 3 through the MCP. So the rule names the route ("grep" inside Bash, `rg`, `findstr`,
  `Select-String`, reading a `.cs` with `cat`), not only the Grep tool.

`core.md` keeps the rule word for word in force (`tests/rules/inventory.tsv`); this file keeps the reason.
If symbol questions drift back to grep, the wording is the first thing to look at.
