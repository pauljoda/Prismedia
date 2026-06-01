// The foliate-js reader engine is vendored under src/lib/vendor and excluded from
// type-checking. Declare its entry module so the dynamic import in BookFileReader
// resolves; the `<foliate-view>` element is used through a loose runtime handle.
declare module "$lib/vendor/foliate-js/view.js";
