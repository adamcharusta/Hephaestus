# Success Metrics

The Owner sets no numeric targets. Hephaestus shows every measure below from the command line and on the Dashboard's statistics page (FR-55), computed from real executions only, and the Owner judges whether it is earning its place. Targets may be added after a month of daily use.

## Primary

| Id | Measure | Validates |
| --- | --- | --- |
| SM-1 | **Unassisted acceptance.** Share of pull requests merged without a Rework and without the Owner changing code by hand. | FR-31 to FR-36, FR-41 |
| SM-2 | **First-pass Validation.** Share of Tasks whose Validation passed before any Correction Loop round. | FR-9, FR-31 |
| SM-3 | **Owner interventions.** Escalations, cancellations, and manual restarts per Task. | FR-9, FR-27 to FR-29, FR-48 |
| SM-4 | **Review effort.** Review comments and Rework rounds per pull request. | FR-32 to FR-34 |

## Secondary

| Id | Measure | Validates |
| --- | --- | --- |
| SM-5 | **Unattended windows.** Share of scheduled windows that ran to their end without needing the Owner. | FR-12, FR-29 |
| SM-6 | **Successful Handoffs.** Share of Handoffs after which the Task reached a pull request. | FR-28 |
| SM-7 | **Self-hosting.** Share of Hephaestus's own merged pull requests that Hephaestus produced. | — |

## Counter-metrics (do not optimize)

| Id | Measure | Counterbalances |
| --- | --- | --- |
| SM-C1 | **Rework rate.** Share of pull requests that needed at least one Rework. | SM-1 and any pressure to open more pull requests |
| SM-C2 | **Wasted review.** Pull requests closed without merging, and failed Tasks the Owner had to read. | Throughput |

## Rules

- The number of pull requests opened is deliberately not a success metric.
- Each figure can be traced to the Tasks it was computed from; a metric with no data is shown as having none, not as zero.
- The time the Owner spends reviewing, and the time a Task would have taken by hand, cannot be measured by Hephaestus. SM-4 is a proxy.
- Hephaestus never presents estimated or simulated figures as measurements.
