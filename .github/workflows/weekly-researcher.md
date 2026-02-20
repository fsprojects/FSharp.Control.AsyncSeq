---
on:
  schedule: daily
  workflow_dispatch:

safe-outputs:
  create-issue:

tools:
  web-search:

---

# Agentic Weekly Researcher (Ruby)

Do a deep research investigation in ${{ github.repository }} repository, and the related industry in general.

- Read selections of the latest code, issues and PRs for this repo.
- Read latest trends and news from the software industry news source on the Web.

Create a new GitHub issue containing a markdown report with interesting news about the area related to this software project, related products and competitive analysis, related research papers, new ideas, market opportunities, business analysis, enjoyable anecdotes.

> NOTE: Include a link like this at the end of the report:

```
> AI-generated content by ${{ github.workflow }}, may contain mistakes.
```
