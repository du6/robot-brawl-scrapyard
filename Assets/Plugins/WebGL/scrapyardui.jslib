mergeInto(LibraryManager.library, {
  ScrapyardUiPixelRatio: function () {
    var canvas = Module.canvas;
    if (!canvas) return 1;
    var width = canvas.getBoundingClientRect().width;
    return width > 0 ? canvas.width / width : 1;
  },
  ScrapyardUiStage: function (ptr) {
    // Capture only (PromoAutopilot): put the route's stage in the document
    // title, which osascript can read from outside. A recording session has no
    // console access to a window it did not open.
    try { document.title = 'SCRAPYARD ' + UTF8ToString(ptr); } catch (e) {}
  },
  ScrapyardUiCoarsePointer: function () {
    return window.matchMedia && window.matchMedia('(pointer: coarse)').matches ? 1 : 0;
  }
});
