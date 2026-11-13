# PickerHub desktop interface

## Reference review

The redesign uses the interaction patterns visible in the official product material for four established color utilities.

- PowerToys Color Picker provides a direct pick action, a recent-color history, and copy controls aligned with individual color formats. https://learn.microsoft.com/en-us/windows/powertoys/color-picker
- Sip gives palettes visual previews and makes collection management a primary part of its desktop interface. https://sipapp.io/
- ColorSlurp places color editing, formats, palettes, and contrast tools close together. https://colorslurp.com/
- Just Color Picker combines precise sampling with color lists and color editing. https://annystudio.com/software/colorpicker/

These references inform the layout and interaction choices. Their screenshots and product artwork are research material.

## Audit and implementation

The previous interface gave almost every action the same visual emphasis, displayed commands as wrapping button rows, and placed important actions below long content. The rebuilt interface uses a persistent sidebar for the existing six tools, one global eyedropper action, a two-column editor and format inspector, contextual palette menus, and task-specific previews.

The design uses Avalonia's existing Fluent foundation, Noto Sans Arabic, Tabler outline icons, and a 12/13/16/25-pixel type hierarchy. Content uses 12-pixel corners and controls use 8-pixel corners. Surface colors are #F6F7FA and #FFFFFF in light mode, #121923 and #1D2938 in dark mode. The existing blue accent remains #2856B4. Actual color samples keep their own colors, with text chosen by the Rust contrast engine.

The desktop brief sets design variance to 5, motion intensity to 2, and visual density to 5. The color spectrum and gradient preview communicate actual editable values. Layouts stack below the available width needed by paired editors. Navigation remains available at the supported minimum window size. Hover, focus, pressed, loading, empty, and error states use the native framework and contextual feedback.
