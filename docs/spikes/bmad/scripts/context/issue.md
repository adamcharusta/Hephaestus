# Report items that are low on stock (#12)

Story 1.2 of Epic 1 in `_bmad-output/planning-artifacts/epics.md`.

As a shop keeper, I want to list the items that are low on stock, so that I can reorder them in time.

## Requirements

- Add the method `Inventory.low_stock(threshold)` to `shelf/inventory.py`.
- It returns the names of the items whose quantity is at or below `threshold`, sorted alphabetically.
- An item with quantity 0 is still in the inventory and is listed.
- A negative `threshold` raises `ValueError`.
- Python standard library only.

## Acceptance criteria

- Given items with quantities 1, 5, and 9, when `low_stock(5)` is called, then it returns the names of the items with quantity 1 and 5, sorted alphabetically.
- Given no item at or below the threshold, when `low_stock(threshold)` is called, then it returns an empty list.
- Given a negative threshold, when `low_stock(threshold)` is called, then `ValueError` is raised.
- Each criterion has a unit test, and `python3 -m unittest discover -s tests -t .` passes.
