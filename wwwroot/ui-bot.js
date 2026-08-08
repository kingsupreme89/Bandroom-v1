// ==========================================================
// BANDROOM UI SECURITY BOT — Automated DOM Bug Scanner
// ==========================================================
// Walks the entire app DOM checking for 12 categories of UI bugs.
// Runs automatically when included, reports via console + toast.
// Call window.__runUIBot() to re-run manually.
// Results stored in window.__uiBotReport.

(function () {
  "use strict";

  const REPORT = {
    timestamp: new Date().toISOString(),
    critical: [],
    warnings: [],
    passes: [],
  };

  function addFinding(level, category, detail, selector) {
    const entry = { level, category, detail, selector };
    if (level === "critical") REPORT.critical.push(entry);
    else if (level === "warning") REPORT.warnings.push(entry);
    else REPORT.passes.push(entry);
  }

  // ─── CHECK 1: [hidden] + display:flex specificity bug ───
  function checkHiddenFlexOverlays() {
    const overlays = document.querySelectorAll("[id$='-overlay'], [id$='-panel'].glass");
    overlays.forEach((el) => {
      const style = window.getComputedStyle(el);
      const hasHidden = el.hasAttribute("hidden");
      const hasDisplayFlex = style.display === "flex";
      const hasHiddenOverride = [...document.styleSheets].some((sheet) => {
        try {
          return [...sheet.cssRules].some(
            (rule) =>
              rule.selectorText &&
              rule.selectorText.includes(el.id + "[hidden]") &&
              rule.style.display === "none"
          );
        } catch (e) {
          return false;
        }
      });

      if (hasDisplayFlex && !hasHiddenOverride && el.id) {
        addFinding(
          "critical",
          "Missing [hidden] CSS override",
          `#${el.id} uses display:flex but has no #[hidden] { display:none } rule. Toggling hidden from JS will silently fail.`,
          `#${el.id}`
        );
      } else if (hasDisplayFlex && hasHiddenOverride) {
        addFinding(
          "pass",
          "[hidden] override exists",
          `#${el.id} correctly has #[hidden] override`,
          `#${el.id}`
        );
      }
    });
  }

  // ─── CHECK 2: Duplicate CSS property declarations ───
  function checkDuplicateCssProperties() {
    // Check for known duplicates in the stylesheet
    const knownDupes = [
      { sel: ".header-right", prop: "gap" },
      { sel: ".update-actions", prop: "margin-top" },
    ];
    knownDupes.forEach(({ sel, prop }) => {
      let count = 0;
      try {
        [...document.styleSheets].forEach((sheet) => {
          try {
            [...sheet.cssRules].forEach((rule) => {
              if (
                rule.selectorText &&
                rule.selectorText.trim() === sel &&
                rule.style[prop]
              ) {
                count++;
              }
            });
          } catch (e) {}
        });
      } catch (e) {}
      if (count > 1) {
        addFinding(
          "warning",
          "Duplicate CSS property",
          `${sel} declares ${prop} ${count} times — only the last declaration wins.`,
          sel
        );
      } else {
        addFinding("pass", "No duplicate found", `${sel} ${prop} declared once`, sel);
      }
    });
  }

  // ─── CHECK 3: Canvas size mismatch ───
  function checkCanvasSizeMismatches() {
    document.querySelectorAll("canvas").forEach((canvas) => {
      const attrW = parseInt(canvas.getAttribute("width"));
      const attrH = parseInt(canvas.getAttribute("height"));
      const cssW = parseFloat(window.getComputedStyle(canvas).width);
      const cssH = parseFloat(window.getComputedStyle(canvas).height);
      if (attrW && cssW && Math.abs(attrW - cssW) > 2) {
        addFinding(
          "critical",
          "Canvas size mismatch",
          `Canvas ${canvas.id || "(no id)"} HTML width=${attrW} but CSS width=${cssW} — rendering will be distorted.`,
          `#${canvas.id || "canvas"}`
        );
      } else if (attrW && cssW) {
        addFinding(
          "pass",
          "Canvas size OK",
          `Canvas ${canvas.id || "(no id)"} matches CSS size`,
          `#${canvas.id || "canvas"}`
        );
      }
    });
  }

  // ─── CHECK 4: innerHTML used with marketplace/user data ───
  function checkInnerHtmlUsage() {
    // This is a code-level check, not runtime. We flag known vulnerable patterns.
    const dangerPatterns = [
      "innerHTML",
      "insertAdjacentHTML",
      "document.write",
    ];
    // We can't inspect JS source at runtime, but we can check rendered
    // marketplace content for unescaped HTML
    const marketplaceEls = document.querySelectorAll(
      ".bandroom-item-name, .bandroom-item-school, #ticker-text"
    );
    let hasHtmlInjection = false;
    marketplaceEls.forEach((el) => {
      // If the text content contains HTML-looking fragments that weren't escaped
      if (el.innerHTML !== el.textContent && el.querySelector("script, img, iframe")) {
        hasHtmlInjection = true;
        addFinding(
          "critical",
          "Potential XSS via innerHTML",
          `Marketplace element contains raw HTML tags that should be escaped.`,
          el.id || el.className
        );
      }
    });
    if (!hasHtmlInjection) {
      addFinding(
        "pass",
        "Marketplace content safe",
        "No unescaped HTML detected in marketplace-rendered content",
        ".bandroom-item-name, .bandroom-item-school"
      );
    }
  }

  // ─── CHECK 5: Missing aria-label on buttons ───
  function checkAccessibility() {
    let missingLabels = 0;
    document.querySelectorAll("button, [role='button']").forEach((btn) => {
      const hasLabel =
        btn.hasAttribute("aria-label") ||
        btn.hasAttribute("title") ||
        (btn.textContent && btn.textContent.trim().length > 0);
      if (!hasLabel) {
        missingLabels++;
        if (missingLabels <= 5) {
          // Cap at 5 to avoid spam
          addFinding(
            "warning",
            "Missing accessibility label",
            `Button has no aria-label, title, or text content — screen readers cannot describe it.`,
            btn.id || btn.className || "<button>"
          );
        }
      }
    });
    if (missingLabels === 0) {
      addFinding("pass", "Accessibility OK", "All buttons have labels", "all buttons");
    } else if (missingLabels > 5) {
      addFinding(
        "warning",
        "Accessibility gaps",
        `${missingLabels} buttons lack labels (first 5 shown above)`,
        "multiple"
      );
    }
  }

  // ─── CHECK 6: Nested overflow scroll areas ───
  function checkNestedOverflow() {
    let nestedCount = 0;
    document.querySelectorAll("*").forEach((el) => {
      const style = window.getComputedStyle(el);
      if (
        (style.overflowY === "auto" || style.overflowY === "scroll") &&
        el.parentElement
      ) {
        const parentStyle = window.getComputedStyle(el.parentElement);
        if (parentStyle.overflowY === "auto" || parentStyle.overflowY === "scroll") {
          nestedCount++;
          if (nestedCount <= 3) {
            addFinding(
              "warning",
              "Nested scroll areas",
              `Element ${el.id || el.className} has overflow-y inside a parent with overflow-y — double scrollbar trap.`,
              `#${el.id || el.className}`
            );
          }
        }
      }
    });
    if (nestedCount === 0) {
      addFinding("pass", "No nested scroll", "No double-scrollbar traps found", "all");
    }
  }

  // ─── CHECK 7: z-index collisions ───
  function checkZIndexCollisions() {
    const zMap = {};
    document.querySelectorAll("*").forEach((el) => {
      const z = window.getComputedStyle(el).zIndex;
      if (z !== "auto") {
        const zNum = parseInt(z);
        if (!zMap[zNum]) zMap[zNum] = [];
        zMap[zNum].push(el.id || el.className || el.tagName);
      }
    });
    let collisions = 0;
    Object.entries(zMap).forEach(([z, els]) => {
      if (els.length >= 3) {
        // 3+ elements sharing same z-index
        collisions++;
        addFinding(
          "warning",
          "z-index collision",
          `${els.length} elements share z-index ${z}: ${els.slice(0, 3).join(", ")}...`,
          `z-index: ${z}`
        );
      }
    });
    if (collisions === 0) {
      addFinding("pass", "z-index clean", "No significant z-index collisions", "all");
    }
  }

  // ─── CHECK 8: Empty images without fallback ───
  function checkEmptyImages() {
    let emptyCount = 0;
    document.querySelectorAll("img").forEach((img) => {
      if (!img.src || img.src === "" || img.src === window.location.href) {
        if (!img.hasAttribute("alt") || img.alt === "") {
          emptyCount++;
          if (emptyCount <= 5) {
            addFinding(
              "warning",
              "Empty/broken image",
              `Image has no src and no alt text — renders as broken icon.`,
              `#${img.id || "img"}`
            );
          }
        }
      }
    });
    if (emptyCount === 0) {
      addFinding("pass", "Images OK", "No broken images found", "all img");
    }
  }

  // ─── CHECK 9: Toast/ticker bottom-edge overlap ───
  function checkBottomEdgeOverlap() {
    const toast = document.querySelector(".toast");
    const ticker = document.querySelector("#bandroom-ticker");
    const preview = document.querySelector("#preview-bar");
    if (toast && ticker) {
      const toastBottom = parseInt(window.getComputedStyle(toast).bottom);
      const tickerHeight = ticker.offsetHeight;
      if (toastBottom <= tickerHeight) {
        addFinding(
          "critical",
          "Toast hidden behind ticker",
          `Toast renders at bottom=${toastBottom}px but ticker is ${tickerHeight}px tall — toasts are invisible.`,
          ".toast, #bandroom-ticker"
        );
      } else {
        addFinding(
          "pass",
          "Toast above ticker",
          `Toast (${toastBottom}px) clears ticker (${tickerHeight}px)`,
          ".toast"
        );
      }
    }
    if (preview && ticker) {
      const previewBottom = parseInt(window.getComputedStyle(preview).bottom);
      if (previewBottom <= ticker.offsetHeight) {
        addFinding(
          "warning",
          "Preview bar too low",
          "Preview bar may overlap with ticker",
          "#preview-bar"
        );
      }
    }
  }

  // ─── CHECK 10: Animation conflicts on same element ───
  function checkAnimationConflicts() {
    let conflicts = 0;
    document.querySelectorAll("*").forEach((el) => {
      const anim = window.getComputedStyle(el).animationName;
      if (anim && anim !== "none") {
        const names = anim.split(",").map((s) => s.trim()).filter(Boolean);
        if (names.length >= 2) {
          conflicts++;
          if (conflicts <= 3) {
            addFinding(
              "warning",
              "Multiple animations",
              `Element ${el.id || el.className} has ${names.length} simultaneous animations: ${names.join(", ")}`,
              `#${el.id || el.className}`
            );
          }
        }
      }
    });
    if (conflicts === 0) {
      addFinding("pass", "Animation clean", "No elements with multiple animations", "all");
    }
  }

  // ─── CHECK 11: Font applied to form controls ───
  function checkFontInheritance() {
    const bodyFont = window.getComputedStyle(document.body).fontFamily;
    let mismatches = 0;
    document
      .querySelectorAll("button, select, input, textarea")
      .forEach((el) => {
        const elFont = window.getComputedStyle(el).fontFamily;
        if (!elFont.includes("Outfit") && !elFont.includes(bodyFont.split(",")[0].replace(/"/g, ""))) {
          mismatches++;
        }
      });
    if (mismatches > 0) {
      addFinding(
        "warning",
        "Font not inherited",
        `${mismatches} form controls do not inherit the Outfit font`,
        "button, select, input, textarea"
      );
    } else {
      addFinding("pass", "Font OK", "All form controls inherit Outfit font", "all controls");
    }
  }

  // ─── CHECK 12: Overlay z-index ordering ───
  function checkOverlayZOrder() {
    const overlayIds = [
      "team-picker-overlay",    // 10
      "matchup-overlay",        // 20
      "onboarding-overlay",     // 20
      "save-profile-overlay",   // 20
      "update-overlay",         // 30
      "songpack-prompt-overlay",// 30
      "songpack-progress-overlay", // 30
      "whats-new-overlay",      // 50
      "bandroom-overlay",       // 60
      "bandroom-album-overlay", // 60
      "my-downloads-overlay",   // 60
      "profile-overlay",        // 65
      "bandroom-upload-overlay",// 65
      "logo-crop-overlay",      // 65
      "bg-crop-overlay",        // 65
    ];
    const zValues = {};
    overlayIds.forEach((id) => {
      const el = document.getElementById(id);
      if (el) {
        zValues[id] = parseInt(window.getComputedStyle(el).zIndex);
      }
    });
    // Verify they're in ascending order
    const entries = Object.entries(zValues).sort((a, b) => a[1] - b[1]);
    let orderOk = true;
    for (let i = 1; i < entries.length; i++) {
      if (entries[i][1] < entries[i - 1][1]) {
        orderOk = false;
        addFinding(
          "warning",
          "Overlay z-order",
          `${entries[i][0]} (z=${entries[i][1]}) appears behind ${entries[i - 1][0]} (z=${entries[i - 1][1]})`,
          entries[i][0]
        );
      }
    }
    if (orderOk) {
      addFinding("pass", "Overlay z-order OK", "All overlays in correct stacking order", "overlays");
    }
  }

  // ─── RUN ALL CHECKS ───
  function runAllChecks() {
    REPORT.critical = [];
    REPORT.warnings = [];
    REPORT.passes = [];
    REPORT.timestamp = new Date().toISOString();

    checkHiddenFlexOverlays();
    checkDuplicateCssProperties();
    checkCanvasSizeMismatches();
    checkInnerHtmlUsage();
    checkAccessibility();
    checkNestedOverflow();
    checkZIndexCollisions();
    checkEmptyImages();
    checkBottomEdgeOverlap();
    checkAnimationConflicts();
    checkFontInheritance();
    checkOverlayZOrder();
  }

  // ─── OUTPUT ───
  function printReport() {
    const total = REPORT.critical.length + REPORT.warnings.length + REPORT.passes.length;
    console.group(
      `%c🔍 UI Bot Scan — ${REPORT.critical.length} critical · ${REPORT.warnings.length} warnings · ${REPORT.passes.length} passes`,
      "font-weight:bold; font-size:14px"
    );

    if (REPORT.critical.length) {
      console.group(`%c🔴 CRITICAL (${REPORT.critical.length})`, "color:#ef4444; font-weight:bold");
      REPORT.critical.forEach((f) =>
        console.log(
          `%c[${f.category}]%c ${f.detail} %c${f.selector}`,
          "color:#ef4444; font-weight:bold",
          "color:#e7ecf1",
          "color:#7d8791"
        )
      );
      console.groupEnd();
    }

    if (REPORT.warnings.length) {
      console.group(`%c🟡 WARNINGS (${REPORT.warnings.length})`, "color:#facc15; font-weight:bold");
      REPORT.warnings.forEach((f) =>
        console.log(
          `%c[${f.category}]%c ${f.detail} %c${f.selector}`,
          "color:#facc15; font-weight:bold",
          "color:#e7ecf1",
          "color:#7d8791"
        )
      );
      console.groupEnd();
    }

    if (REPORT.passes.length) {
      console.group(`%c🟢 PASSES (${REPORT.passes.length})`, "color:#22c55e; font-weight:bold");
      REPORT.passes.forEach((f) =>
        console.log(
          `%c✓ ${f.category}%c — ${f.detail}`,
          "color:#22c55e",
          "color:#7d8791"
        )
      );
      console.groupEnd();
    }

    console.groupEnd();

    // Toast summary
    if (REPORT.critical.length > 0) {
      try {
        const toastFn = typeof showToast === "function" ? showToast : null;
        if (toastFn) {
          toastFn(`UI Bot: ${REPORT.critical.length} critical, ${REPORT.warnings.length} warnings found`);
        }
      } catch (e) {}
    }
  }

  // ─── EXPOSE ───
  window.__runUIBot = function () {
    runAllChecks();
    printReport();
    window.__uiBotReport = REPORT;
    return REPORT;
  };

  window.__uiBotReport = REPORT;

  // Auto-run on DOM ready
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", () => {
      setTimeout(window.__runUIBot, 1500); // Wait for dynamic content to render
    });
  } else {
    setTimeout(window.__runUIBot, 1500);
  }
})();