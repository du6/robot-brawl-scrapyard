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
  // The four safe-area insets, in FRAMEBUFFER pixels so they can be compared
  // with Screen.width/height directly, like Screen.safeArea on a device.
  //
  // They exist because the page sets viewport-fit=cover (index.html:5), which
  // deliberately puts the canvas UNDER a notch and a home indicator, while
  // Unity's Screen.safeArea in a WebGL build is the whole screen - so every
  // safe-area mechanism in the game computed zero and drew controls into the
  // cut-out. env() is only readable from CSS, hence the hidden probe element.
  $sy_safe__deps: [],
  $sy_safe: function (side) {
    var d = document.getElementById('__sy_safe_probe');
    if (!d) {
      d = document.createElement('div');
      d.id = '__sy_safe_probe';
      d.style.cssText = 'position:fixed;left:0;top:0;width:0;height:0;' +
        'visibility:hidden;pointer-events:none;' +
        'padding-left:env(safe-area-inset-left,0px);' +
        'padding-right:env(safe-area-inset-right,0px);' +
        'padding-top:env(safe-area-inset-top,0px);' +
        'padding-bottom:env(safe-area-inset-bottom,0px);';
      document.body.appendChild(d);
    }
    var cs = window.getComputedStyle(d);
    var css = parseFloat(cs.getPropertyValue('padding-' + side)) || 0;
    var canvas = Module.canvas;
    var ratio = 1;
    if (canvas) {
      var w = canvas.getBoundingClientRect().width;
      if (w > 0) ratio = canvas.width / w;
    }
    return css * ratio;
  },
  ScrapyardUiSafeLeft__deps: ['$sy_safe'],
  ScrapyardUiSafeLeft: function () { return sy_safe('left'); },
  ScrapyardUiSafeRight__deps: ['$sy_safe'],
  ScrapyardUiSafeRight: function () { return sy_safe('right'); },
  ScrapyardUiSafeTop__deps: ['$sy_safe'],
  ScrapyardUiSafeTop: function () { return sy_safe('top'); },
  ScrapyardUiSafeBottom__deps: ['$sy_safe'],
  ScrapyardUiSafeBottom: function () { return sy_safe('bottom'); },
  ScrapyardUiCoarsePointer: function () {
    return window.matchMedia && window.matchMedia('(pointer: coarse)').matches ? 1 : 0;
  }
});
