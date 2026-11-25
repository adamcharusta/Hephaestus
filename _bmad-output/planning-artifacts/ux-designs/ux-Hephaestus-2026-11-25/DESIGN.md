---
name: Hephaestus
description: Visual identity of the Hephaestus Dashboard, a read-only, local web view of a self-hosted coding-agent orchestrator. Angular Material (Material 3) with the CDK; this file specifies the brand layer on top of it.
status: draft
sources:
  - ../../prds/prd-Hephaestus-2026-11-25/prd.md
  - ../../architecture/architecture-Hephaestus-2026-11-25/ARCHITECTURE-SPINE.md
  - ../../sprint-change-proposal-2026-11-25.md
updated: 2026-11-25
colors:
  # Neutral base. Light theme first, dark theme with the -dark suffix.
  background: '#FAFAF9'
  surface: '#FFFFFF'
  surface-muted: '#F1F1EF'
  border: '#D9D9D6'
  text: '#1C1D1F'
  text-muted: '#5C5F66'
  background-dark: '#131416'
  surface-dark: '#1B1D20'
  surface-muted-dark: '#24272B'
  border-dark: '#33373D'
  text-dark: '#E8E9EB'
  text-muted-dark: '#A3A7AE'
  # The one accent: ember. Active and interactive elements only.
  ember: '#B8440B'
  on-ember: '#FFFFFF'
  ember-dark: '#FF9A5C'
  on-ember-dark: '#2B1200'
  # Task states. Never used for anything but a Task state.
  state-queued: '#5C5F66'
  state-running: '#1D5FBF'
  state-waiting-for-owner: '#7A5F00'
  state-waiting-for-capacity: '#6B4BB8'
  state-pull-request-open: '#0B6E6E'
  state-merged: '#1E6B3A'
  state-failed: '#B3261E'
  state-ended: '#6E7178'
  state-queued-dark: '#A3A7AE'
  state-running-dark: '#7FB0F5'
  state-waiting-for-owner-dark: '#E8C547'
  state-waiting-for-capacity-dark: '#B9A2F2'
  state-pull-request-open-dark: '#5CC8C8'
  state-merged-dark: '#6FCF97'
  state-failed-dark: '#F2867E'
  state-ended-dark: '#8B8F96'
typography:
  body:
    fontFamily: 'system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif'
    fontSize: 14px
    fontWeight: '400'
    lineHeight: '1.5'
  label:
    fontFamily: 'system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif'
    fontSize: 12px
    fontWeight: '500'
    lineHeight: '1.35'
    letterSpacing: 0.02em
  title:
    fontFamily: 'system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif'
    fontSize: 20px
    fontWeight: '600'
    lineHeight: '1.3'
  headline:
    fontFamily: 'system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif'
    fontSize: 24px
    fontWeight: '600'
    lineHeight: '1.25'
  mono:
    fontFamily: 'ui-monospace, "Cascadia Mono", "SF Mono", Menlo, Consolas, "Liberation Mono", monospace'
    fontSize: 13px
    fontWeight: '400'
    lineHeight: '1.5'
rounded:
  sm: 4px
  md: 8px
  lg: 12px
  full: 9999px
spacing:
  '1': 4px
  '2': 8px
  '3': 12px
  '4': 16px
  '6': 24px
  '8': 32px
  page-margin: 24px
  nav-rail: 72px
components:
  nav-item-active:
    indicator: '{colors.ember}'
    foreground: '{colors.text}'
    radius: '{rounded.full}'
  link:
    foreground: '{colors.ember}'
  focus-ring:
    color: '{colors.ember}'
    width: 2px
  state-chip:
    background: '{colors.surface-muted}'
    foreground: '{colors.text}'
    radius: '{rounded.full}'
    typography: '{typography.label}'
  data-table:
    header-background: '{colors.surface-muted}'
    row-border: '{colors.border}'
    row-height: 40px
  step-timeline:
    line: '{colors.border}'
    spacing: '{spacing.4}'
  output-viewer:
    background: '{colors.surface-muted}'
    foreground: '{colors.text}'
    typography: '{typography.mono}'
    radius: '{rounded.md}'
  live-indicator:
    connected: '{colors.state-merged}'
    reconnecting: '{colors.state-waiting-for-owner}'
    radius: '{rounded.full}'
  act-elsewhere-hint:
    background: '{colors.surface-muted}'
    border: '{colors.border}'
    radius: '{rounded.md}'
---

## Brand & Style

Hephaestus is a tool an engineer leaves running and looks at now and then. The Dashboard is a window into a forge, not the forge itself: it shows what is being made, by which Agent, and how it was checked, and it changes nothing. The look follows from that. It is quiet, dense enough to read a day's work on one screen, and neutral almost everywhere, so that the few things that carry meaning stand out: the state of a Task, and the one warm accent that marks what is active.

The references to craftsmanship and the mythological namesake are restrained, as the PRD requires. In the visual layer there is exactly one: the ember accent. There are no anvils, flames, hammers, textures, or gradients.

The Dashboard inherits Angular Material (Material 3) and the CDK as they ship. This file specifies the brand layer only: the neutral base, the ember accent, the Task state colours, the type stacks, tighter corners, and a handful of components Material does not have. Material components that are not listed under Components are used unchanged.

Everything is bundled. No font, icon, stylesheet, or script is loaded from another origin (architecture AD-17).

`[ASSUMPTION]` Every hex value in the frontmatter was drafted without the owner choosing it from rendered options. The decisions the owner made are the structure: light and dark following the system, a neutral base, one ember accent, separate state colours.

## Colors

Two themes, light and dark. The Dashboard follows the system setting and offers a switch; the choice is remembered on that browser. Dark tokens carry the `-dark` suffix and replace their light counterpart one for one.

- **Neutral base** (`background`, `surface`, `surface-muted`, `border`, `text`, `text-muted`). Carries all chrome, tables, and text. `surface` is for cards and tables on `background`; `surface-muted` is for table headers, the output viewer, and hint boxes.
- **Ember** (`{colors.ember}` light, `{colors.ember-dark}` dark). The only accent. It marks what the Owner can act on or is currently on: links, the selected navigation item, the focus ring, the selected tab, the primary button of the token screen. It replaces Material's primary colour role. It is never a Task state, never a warning, and never decoration.
- **Task state colours** (`state-*`). One colour per state, used only in the state chip's icon and left edge and in the step timeline. `state-ended` serves the three quiet end states: cancelled, withdrawn, and closed.

| State | Token | Reads as |
| --- | --- | --- |
| `queued` | `state-queued` | waiting its turn |
| `running` | `state-running` | being worked now |
| `waiting-for-owner` | `state-waiting-for-owner` | needs the Owner |
| `waiting-for-capacity` | `state-waiting-for-capacity` | needs an Agent |
| `pull-request-open` | `state-pull-request-open` | ready for review |
| `merged` | `state-merged` | done |
| `failed` | `state-failed` | did not succeed |
| `cancelled`, `withdrawn`, `closed` | `state-ended` | ended without a result |

Colour is never the only carrier of a state: every chip has an icon and the state's name.

Text on `background` and `surface` meets WCAG 2.2 AA (4.5:1) in both themes, including `text-muted`; state colours meet 3:1 against `surface` as graphical elements. `[ASSUMPTION]` The values were chosen to meet these ratios by calculation and must be confirmed with a contrast check when the theme file is first built.

Avoid: a second accent, ember used as a state, state colours used for charts or chrome, tinted surfaces, gradients.

## Typography

System font stacks, not a bundled typeface: the Dashboard looks native on the machine it runs on and ships no font file. `[ASSUMPTION]`

| Role | Token | Use |
| --- | --- | --- |
| Headline | `{typography.headline}` | The name of the surface, once per page |
| Title | `{typography.title}` | Section and card titles |
| Body | `{typography.body}` | Tables, prose, descriptions |
| Label | `{typography.label}` | Column headers, chips, field names, timestamps |
| Mono | `{typography.mono}` | Ids, commit ids, branch names, commands, Agent output, Validation output |

Anything the Owner might copy into a terminal or into GitHub is set in `mono`. Agent-written prose, such as a Finding or a plan, is set in `body`.

## Layout & Spacing

A 4-pixel scale (`spacing.1` to `spacing.8`). Page margin is `{spacing.page-margin}`.

The frame is a navigation rail on the left (`{spacing.nav-rail}` wide, icon above label), a top bar across the content, and one content column that uses the full remaining width. Tables are the main content and are allowed to be wide. Text-only content, such as a plan, is capped at 80 characters per line.

Material's compact density is used for tables and lists; a table row is 40 pixels high.

## Elevation & Depth

Flat. Surfaces are separated by `border` and by the step from `background` to `surface`, not by shadows. Material's elevation is kept only where something floats above the page: menus, tooltips, and the side sheet that shows a step's output.

## Shapes

`{rounded.sm}` for chips' inner elements and inputs, `{rounded.md}` for cards, tables, and the output viewer, `{rounded.lg}` for the side sheet and dialogs, `{rounded.full}` for state chips, the navigation indicator, and the live indicator. Tighter than Material's defaults, so the surface reads as a tool.

## Components

Used from Angular Material unchanged: navigation list, toolbar, table with sort and paginator, tabs, chips, tooltip, menu, side sheet (sidenav), progress bar, snack bar, form field, button, icon. Icons are Material Symbols, bundled as SVG and registered locally. `[ASSUMPTION]`

Brand-layer components:

- **State chip.** A pill on `surface-muted` with a state icon in the state's colour, a 3-pixel left edge in the same colour, and the state's name in `label`. One size. The same chip is used in every table, header, and timeline.
- **Navigation item, active.** Material's navigation rail item with the indicator in `{colors.ember}`.
- **Step timeline.** A vertical line in `border` with one node per step run. The node carries the step kind's icon; a failed step run's node uses `state-failed`, a running one `state-running`, all others `text-muted`. Each node has a one-line summary: step kind, Agent, Model, Effort, duration, outcome.
- **Output viewer.** A block on `surface-muted` in `mono`, with line numbers in `text-muted` and no syntax colouring. Masked values appear as the mask the server stored; they are not restyled.
- **Live indicator.** A small dot with a label in the top bar. `live-indicator.connected` with "Live"; `live-indicator.reconnecting` with "Reconnecting". It does not pulse or animate.
- **Act-elsewhere hint.** A bordered box on `surface-muted` holding one sentence and either a link to GitHub or a command in `mono` with a copy button. It is how a read-only Dashboard says where an action is taken.
- **Copyable value.** A `mono` value with a copy icon that appears on hover and on focus.

## Do's and Don'ts

| Do | Don't |
| --- | --- |
| Use ember for what is interactive or currently selected | Use ember for a state, a warning, or decoration |
| Show a Task state with its chip: colour, icon, and name together | Show a state by colour alone, or invent a colour for a new state without adding a token |
| Keep both themes in step: every colour has a light and a dark value | Hard-code a hex value in a component |
| Bundle every font, icon, and stylesheet | Load anything from another origin |
| Separate surfaces with borders | Add shadows to cards or tables |
| Set copyable values and output in `mono` | Colour or re-format Agent output |
| Use Material components as they ship | Restyle a Material component beyond tokens |
| Keep forge imagery to the ember accent | Add flames, anvils, textures, or gradients |
