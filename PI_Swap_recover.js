
/*
 * PixInsight Swap Image Recovery
 * Original script Copyright 2025 John Scott
 * Licensed under the MIT License.
 *
 * Modified version:
 * - Correct Windows path handling
 * - Skip segmented continuation files
 * - Handle recoverable errors
 * - Recover selected files individually
 */

#include <pjsr/UndoFlag.jsh>

// Swap directory - use forward slashes on Windows.
let swap = "C:/Users/zakfo/Documents/PI_TempSwamp";

// Open file selection dialog.
let ofd = new OpenFileDialog();
ofd.multipleSelections = true;
ofd.caption = "Select Primary Swap Files";
ofd.filters = [["Swap Files", "*.swp"]];
ofd.initialPath = swap;

if (ofd.execute())
{
   for (let i = 0; i < ofd.fileNames.length; i++)
   {
      let fileName = ofd.fileNames[i];

      // Skip continuation segments.
      if (/-part\d+\.swp$/i.test(fileName))
      {
         console.writeln(
            "Skipping continuation file: " + fileName
         );
         continue;
      }

      console.writeln("Attempting recovery: " + fileName);

      let w = null;
      let processing = false;

      try
      {
         w = new ImageWindow(1, 1);

         let view = w.mainView;

         view.beginProcess(UndoFlag_NoSwapFile);
         processing = true;

         // Read the primary swap image.
         view.image.readRawFile(fileName);

         view.endProcess();
         processing = false;

         view.id = "recov_" + getOriginalIdentifier(fileName);

         w.show();
         w.zoomToOptimalFit();

         console.writeln(
            "Successfully recovered: " + view.id
         );
      }
      catch (e)
      {
         console.criticalln(
            "Recovery failed: " + fileName
         );
         console.criticalln(String(e));

         if (w !== null)
         {
            try
            {
               if (processing)
                  w.mainView.endProcess();

               w.forceClose();
            }
            catch (cleanupError)
            {
               console.warningln(
                  "Unable to clean up image window."
               );
            }
         }
      }
   }
}
else
{
   console.writeln("File selection cancelled.");
}

// Extract the original image identifier.
function getOriginalIdentifier(swapFileName)
{
   let fullName = File.extractName(swapFileName);
   let parts = fullName.split("~");

   if (parts.length === 0)
      return "unknown";

   return parts[parts.length - 1];
}
