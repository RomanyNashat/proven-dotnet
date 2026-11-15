`Referrals/` is a service that has been running for a while. Nobody wrote down its rules, and we are about to refactor it.

Write down the business rules it enforces today, as it runs, into `Referrals/rules.md`: one rule per line, `R1: Given ..., when ..., then ... (File.cs:line)`, naming the line of code that enforces it. Anything that looks like a rule but isn't enforced when the service runs goes under a `## Not enforced` heading, with why. Write only `rules.md`: don't change the code and don't write tests for this.
