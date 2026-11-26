# Change: the shop also sells goods by weight

Raised while Story 1.2 was in progress.

The shop keeper now sells nails and seed by weight, so a quantity can be a
fraction, for example 2.5 kilograms. The plan says quantities are integers
(architecture) and Story 1.1 rejects anything else.

What is known:

- Quantities must accept positive decimal numbers as well as integers.
- Story 1.1 is done and its tests reject `1.5`.
- Story 1.2 compares quantities with a threshold, which may then be a decimal too.
- Nothing else changes: still no persistence, no prices, standard library only.
