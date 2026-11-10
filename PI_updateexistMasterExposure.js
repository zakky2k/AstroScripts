// Set this to the exposure of each original dark frame.
var seconds = 60;

var window = ImageWindow.activeWindow;
if (window.isNull)
   throw new Error("Open and activate the actual master dark image first.");

if (!isFinite(seconds) || seconds <= 0)
   throw new Error("Exposure must be a positive finite number.");

var view = window.mainView;

// Support both newer and older PJSR constant styles.
// 11 = Float64; 0xA0 = Storable | Permanent.
var float64Type =
   typeof PropertyType !== "undefined" ? PropertyType.Float64 : 11;

var attributes =
   typeof PropertyAttribute !== "undefined"
      ? PropertyAttribute.Storable | PropertyAttribute.Permanent
      : 0xA0;

var identifiers = [
   "Instrument:ExposureTime",
   "Instrument:FrameExposureTime"
];

for (var i = 0; i < identifiers.length; ++i)
{
   var id = identifiers[i];

   console.writeln(
      id + ": " +
      (view.hasProperty(id) ? view.propertyValue(id) : "(absent)") +
      " -> " + seconds
   );

   view.setPropertyValue(id, seconds, float64Type, attributes);
}

console.writeln(
   "Properties updated in memory. Save the master dark to a new XISF file."
);
