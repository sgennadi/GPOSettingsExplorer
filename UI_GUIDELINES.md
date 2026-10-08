# UI and HiDPI rules

These rules are mandatory for every existing and new window in GPO Settings Explorer.

## Single source of UI styling

- Use `Themes/WindowsCompact.xaml` and `UiStyle` as the shared UI layer.
- Do not create local application fonts or arbitrary font sizes inside individual windows.
- Monospace/code views must use `UiStyle.MonospaceFontFamily` and `UiStyle.MonospaceFontSize`.
- Visual hierarchy, common spacing, control minimum sizes, wrapping behavior, and reusable styles belong in the shared UI layer.

## Adaptive layout

- Do not use `ResizeMode.NoResize`.
- Use `WrapPanel` for horizontal command/button groups that can exceed the available width.
- Use `MinHeight` instead of fixed `Height` for multiline text editors.
- Long labels and explanatory text must wrap and must not use ellipsis trimming.
- Data grids must keep horizontal and vertical scrolling available where content can exceed the viewport.
- Dialog content that can become taller after wrapping must remain vertically scrollable.
- Fixed widths are allowed only when they describe a real semantic column/control requirement and the surrounding layout can still shrink or scroll safely.

## DPI and screen-size verification

Every UI change must be checked at:

- 100%
- 125%
- 150%
- 175%
- 200%

Also verify moving the window between monitors with different scaling.

At every scale:

- no clipped text;
- no clipped buttons;
- no clipped text boxes, combo boxes, check boxes, or radio buttons;
- no inaccessible bottom/right content;
- no overlapping controls;
- wrapped labels remain readable;
- command bars wrap instead of disappearing;
- dialogs stay within the current monitor work area;
- scrolling remains available when the logical viewport is smaller than the designed window.

## Definition of done

A new GPP section or other feature is not UI-complete until its new and changed windows comply with this checklist.

## Semantic colors and script editing

- All colors and language syntax tokens must come from the centralized palette in `Themes/WindowsCompact.xaml`, exposed through `UiStyle`; never scatter hexadecimal colors across code-behind.
- Keep file type identity, disabled GPO states, missing/unassigned script warnings, and syntax diagnostics distinguishable using BOTH color and textual status.
- Error color means actual failure; warning color means a partial scope, possibly unsafe script, or action requiring attention.
- Script editor must not execute source code in order to preview or validate it. The PowerShell AST parse is an explicit parse-only operation.
- At 200% scaling, editor toolbars should scroll if they cannot wrap within the available height, and Save/Close buttons remain reachable.
- Never drop edited text on a failed save or a hidden GPO change-preview dialog.
