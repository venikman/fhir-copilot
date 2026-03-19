# Agent Architecture Report

## System Overview

The FHIR Copilot uses a **keyword-based intent router** to dispatch user queries to 6 specialized agents. Each agent has a curated set of FHIR tools and domain-specific instructions. The `clinical` agent serves as the **fallback** for ambiguous queries.

```
                         User Query
                             |
                    [ KeywordIntentRouter ]
                     /   |    |   |   \    \
                    /    |    |   |    \    \
              lookup search analytics clinical cohort export
                |      |       |        |       |      |
                +------+-------+--------+-------+------+
                             |
                       [ FhirToolbox ]
                             |
                     [ HttpFhirBackend ]
                             |
                      FHIR R4 Server
```

## Router → Agent Mapping

| Keyword Triggers | Routed Agent | Fallback? |
|---|---|---|
| `show me`, `read`, `what is`, `who manages`, `what insurance`, `coverage for`, `patient/`, `encounter/`, `condition/`, `observation/`, `group/` | **lookup** | |
| `find patients`, `search`, `encounters for`, `patients by`, `list encounters`, `list patients` | **search** | |
| `how many`, `count`, `compare`, `breakdown`, `trend`, `top`, `volume`, `ratio`, `percentage` | **analytics** | |
| `clinical summary`, `summary`, `summarize`, `tell me about`, `what happened`, `full summary`, `plain english` | **clinical** | Default fallback |
| `without`, `who needs`, `care gap`, `gap`, `at risk`, `patients with`, `patients without`, `flag for review` | **cohort** | |
| `export`, `bulk`, `download all`, `snapshot`, `extract` | **export** | |

Routing algorithm: normalize query to lowercase → count keyword hits per agent → highest score wins (ties broken alphabetically) → fall back to `clinical` if zero matches.

## Agent Profiles

### lookup — Single Resource Reads
**Purpose:** Single-resource reads and reference resolution

| Instruction | Detail |
|---|---|
| Resolve references | Convert FHIR references into human-readable names before answering |
| Prefer deterministic reads | Favor direct reads over broad searches |
| Cite resource ids | Include resource IDs in every answer |

**Response sections:** Answer, Evidence

---

### search — Single-Resource Filtering
**Purpose:** Single-resource filtering and search parameter translation

| Instruction | Detail |
|---|---|
| Smallest valid search | Translate user request into the narrowest single-resource search |
| Clinical language mapping | diabetes → E11*, hypertension → I10, HbA1c → 4548-4, metformin → RxNorm 860975 |
| Stay single-resource | Do not convert into cross-resource cohort analysis |

**Response sections:** Answer, Matching resources

---

### analytics — Quantitative Insights
**Purpose:** Counts, comparisons, simple ratios, and rankings over FHIR-derived results

| Instruction | Detail |
|---|---|
| Use calculator | For counting, ranking, summing, percentages, and arithmetic |
| Quantitative insights | Convert structured results into quantitative answers |
| Scope boundary | Limit to simple ratios/breakdowns; cross-resource rollup belongs to cohort |

**Response sections:** Answer, Computation, Evidence

---

### clinical — Multi-Resource Summaries (Fallback)
**Purpose:** Multi-resource patient summaries and plain-English encounter narratives

| Instruction | Detail |
|---|---|
| Synthesize multi-resource | Combine demographics, conditions, medications, observations, encounters, allergies |
| Preserve clinical facts | Cite resource IDs, use domain terminology |
| Default fallback | Handles general inquiries when no other agent matches |

**Response sections:** Demographics, Conditions, Medications, Observations, Encounters, Allergies

---

### cohort — Population-Level Analysis
**Purpose:** Cross-resource set logic, care gaps, and at-risk population identification

| Instruction | Detail |
|---|---|
| Composite criteria | Identify patients matching criteria across multiple resource types |
| Care gap detection | Flag gaps like diabetic patients without recent HbA1c |
| Risk ranking | Rank populations by risk signals (e.g., multiple readmissions) |

**Response sections:** Population, Criteria, Gap or risk signal, Evidence

---

### export — Bulk Export Management
**Purpose:** Bulk export lifecycle management and export-result summarization

| Instruction | Detail |
|---|---|
| Check export status | Summarize resource counts from bulk exports |
| Cite identifiers | Include group ID and export format |
| Stay scoped | Do not attempt searches outside the export context |

**Response sections:** Target, Status, Resource counts

## Tool Access Matrix

| Tool | lookup | search | analytics | clinical | cohort | export |
|---|:---:|:---:|:---:|:---:|:---:|:---:|
| `search_groups` | x | | | x | | x |
| `read_resource` | x | x | x | x | x | x |
| `list_resources` | x | | x | x | x | |
| `search_patients` | | x | x | x | x | |
| `search_encounters` | | x | x | x | x | |
| `search_conditions` | | x | x | x | x | |
| `search_observations` | | x | x | x | x | |
| `search_medications` | | x | x | x | x | |
| `search_allergies` | | x | x | x | x | |
| `calculator` | | | x | x | x | |
| `bulk_export` | | | | x | | x |
| **Total tools** | **3** | **7** | **9** | **11** | **9** | **3** |

### Tool Access Patterns

- **`read_resource`** — Universal. Every agent can read a single FHIR resource by type/id.
- **`search_*` (6 tools)** — Shared by search, analytics, clinical, cohort. Not available to lookup (it reads, not searches) or export (scoped to bulk ops).
- **`calculator`** — Available to analytics, clinical, cohort. Used for arithmetic on result sets.
- **`search_groups`** — Available to lookup, clinical, export. Groups represent attribution lists.
- **`bulk_export`** — Only clinical and export. The most restricted tool.
- **`list_resources`** — Available to lookup, analytics, clinical, cohort. Broad enumeration of a resource type.

### Agent Specialization Spectrum

```
Narrow scope                                              Broad scope
    |                                                         |
    lookup (3)    export (3)    search (7)    analytics (9)   cohort (9)    clinical (11)
    |             |             |             |               |             |
    Single read   Bulk ops      One resource  Quantify        Cross-resource Everything
                  only          type          results         set logic      + fallback
```

## Agent Relationships

### Escalation Paths

1. **lookup → search**: When a single read isn't enough, user needs filtering
2. **search → cohort**: When single-resource filtering needs cross-resource criteria
3. **analytics → cohort**: When simple ratios need population-level logic (care gaps, risk flags)
4. **Any → clinical**: Fallback for ambiguous queries; has access to all 11 tools

### Boundary Rules (from agent instructions)

| Agent | Explicitly defers to |
|---|---|
| search | cohort — "if the user asks for combined criteria across multiple resource types" |
| analytics | cohort — "cross-resource rollup belongs to cohort" |
| export | (none) — "do not attempt searches outside the export context" |
| lookup | (none) — "prefer deterministic reads over broad searches" |

### Overlap Analysis

| Agent Pair | Shared Tools | Differentiation |
|---|---|---|
| analytics ↔ cohort | 9 (identical) | analytics = simple ratios; cohort = cross-resource set logic + care gaps |
| search ↔ analytics | 7 | search = filtering; analytics = counting/ranking results |
| clinical ↔ cohort | 9 | clinical = narrative summaries; cohort = population criteria + risk flags |
| lookup ↔ export | 2 (`search_groups`, `read_resource`) | lookup = read one resource; export = bulk lifecycle |

---

*Generated 2026-03-19 from agent profiles in `config/agents/*.json`*
