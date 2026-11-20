# Using PickerHub in real work

Use PickerHub 1.0.0 on GNOME for one week of your usual design or development work. The goal is to find where a routine task takes extra clicks, interrupts the app you were using, or feels slow often enough to fix.

## Capture colors while working

On Wayland, choose **Start screen sharing** from the tray or **Capture mode** beside the eyedropper. Select all connected displays in the system prompt and check for **Screen sharing active**. The tray actions share that session. Use **Stop screen sharing** when you’re finished with live capture.

| When you need to… | Choose… | What happens |
| --- | --- | --- |
| Paste a color into the app you’re using | **Quick Pick** | PickerHub copies the color silently and keeps its workbench hidden. Paste directly into your design app or code editor. |
| Copy a color and keep working in PickerHub | **Pick and copy** | PickerHub returns to the current tool and confirms the color and copied format. |
| Adjust the color before using it | **Pick and edit** | The Color Editor opens with HEX selected for editing. Your existing clipboard content is preserved. |
| Collect several colors for one project | **Multi-pick into current palette** | Each click adds a swatch to the current palette. Check the destination before starting and press **Esc** to finish. |

If you use Quick Pick frequently, enable **Global shortcut from any window** and **Global shortcut copies without bringing PickerHub forward** in Settings.

## Use the colors in a project

1. In **Palettes**, create a palette for a current project. Assign it a project name through **Palette tools → Project**. Add a few real swatches, then use **Search projects** later to recover one by name or HEX.
2. Open a reference image in **Analyze image**. Click the specific pixel you want, or focus the image and use the arrow keys and **Enter**. Use **Copy color**, **Open in editor**, or **Color actions** on the sample. If a large pasted image shows the reduced-preview notice, open the original file for source-resolution sampling.
3. From a swatch’s **Color actions**, choose **Use as text** or **Use as background**. Repeat with the other color. Review the contrast result, then choose **Save text and background roles** and select the project palette. Find those roles under **Palette tools → Semantic color roles**, where you can export CSS or design tokens.

## Measure a slow palette search

1. Enable **Log errors and timings locally** under **Settings → Support and diagnostics**. Keep your usual display scaling and note the palette size and whether you used Grid or List.
2. Open a large palette. After a few warm-up searches, alternate two searches that show different swatches. Paste one term at a time and wait for the results to settle. Aim for about 30 measurements in one session. Use the search field inside the palette for this check.
3. Choose **Save support report** before quitting or disabling logging. The report keeps up to 64 events from the current session. Take the last 30 `palette-search-to-frame` entries and sort their `milliseconds` values. The 29th value is the nearest-rank p95. This timing ends at a frame callback after updating the results. Compare it with the 100 ms target and include the palette size.

## Record friction worth fixing

A short note is enough. Record the action you chose, what you expected, what happened, and whether it repeated. For capture issues, include whether sharing was already active and which app had focus before and after. For search issues, include the palette size, view, and any exported timings.

> **Example**  
> **Task** Copy a swatch into my editor using Quick Pick.  
> **Expected** The editor stays in focus and I can paste HEX immediately.  
> **Observed** PickerHub came to the front on the second pick.  
> **Context** GNOME Wayland, sharing already active. Happened twice in five picks.

Use **Save support report** after reproducing a problem if you want to include diagnostics. The report excludes your colors, images, clipboard contents, search text, and file paths. Review it before sharing.
