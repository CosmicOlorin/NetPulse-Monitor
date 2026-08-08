# NetPulse Monitor 1.0.5

## Improvements

- The live MR600 monitor now automatically retakes and retains the router's
  single management session after a browser or TP-Link app replaces it.
- LTE History highlights the currently used profile in green.
- The current time-of-day group is always shown before the other periods,
  without disabling sorting inside each group.
- Cell Lock lists stable, previously observed band/EARFCN sets first and fills
  any known PCI/CID when the user selects one.
- The main window has a substantially smaller screen-relative minimum size while
  keeping navigation and primary labels visible.

## Preserved behavior

- LTE profiles with less than five connected minutes remain hidden from the
  user but continue accumulating internal evidence.
- LTE History refreshes preserve the user's scroll position.
- Cell/band writes remain guarded, confirmed and rollback protected. Merely
  choosing an observed set never changes the router.
- Router passwords, SMS content and credentials are never written to logs.
