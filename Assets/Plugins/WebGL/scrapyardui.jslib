mergeInto(LibraryManager.library, {
  ScrapyardUiPixelRatio: function () {
    var canvas = Module.canvas;
    if (!canvas) return 1;
    var width = canvas.getBoundingClientRect().width;
    return width > 0 ? canvas.width / width : 1;
  },
  ScrapyardUiCoarsePointer: function () {
    return window.matchMedia && window.matchMedia('(pointer: coarse)').matches ? 1 : 0;
  }
});
