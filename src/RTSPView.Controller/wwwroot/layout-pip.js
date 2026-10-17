// Layout previews include automation-only overlays without changing their live state.
const layoutPip = (() => {
  const clamp = (value, min, max) => Math.max(min, Math.min(max, value));
  const all = config => [config.doorbellOverlay, config.garageOverlay, ...(config.additionalOverlays || [])]
    .filter(o => o?.camera && !(config.deletedOverlaySlots || []).includes(o.camera.slot));
  const slot = (layout, tile, previews) => tile.cameraSlot > 0 ? tile.cameraSlot : previews.get(layout.id + ':' + tile.cameraSlot);
  const forHost = (config, host) => all(config).filter(o => o.hostCameraSlot === host);
  const mode = overlay => overlay.camera.enabled ? 'Always visible' : 'Automation only';
  function open(overlay) {
    adminLayout.select('overlays');
    const slot = overlay.camera.slot;
    adminLayout.selectOverlay(slot === 10 ? 'doorbell' : slot === 11 ? 'garage' : 'overlay-' + slot);
  }
  // Match OverlayGeometry.Calculate in the viewer, including its 16:9 reference.
  function bounds(o, width, height, image = {left:0, top:0, width, height}) {
    const wp = clamp(o.viewportWidthPercent ?? 50, 10, 95), hp = clamp(o.viewportHeightPercent ?? 50, 10, 95);
    const aspect = [1, 3].includes(o.viewportShape) ? 1 : 16 / 9 * wp / hp;
    const w = Math.min(width * wp / 100, height * hp / 100 * aspect), h = w / aspect;
    return {left: clamp(image.left + (image.width - w) * clamp(o.viewportHorizontalPositionPercent ?? 0, 0, 100) / 100, 0, width - w),
      top: clamp(image.top + (image.height - h) * clamp(o.viewportVerticalPositionPercent ?? 100, 0, 100) / 100, 0, height - h), width: w, height: h};
  }
  function path(o, b) {
    const p = new Path2D(), {left: x, top: y, width: w, height: h} = b;
    if (o.viewportShape === 5 && o.customViewportPathData) {
      try {
        const matrix = new DOMMatrix().translate(x + w / 2, y + h / 2).rotate(o.customViewportRotationDegrees || 0)
          .translate(-w / 2, -h / 2).scale(w / o.customViewportViewBoxWidth, h / o.customViewportViewBoxHeight)
          .translate(-o.customViewportViewBoxX, -o.customViewportViewBoxY);
        p.addPath(new Path2D(o.customViewportPathData), matrix);return p;
      } catch { /* A missing mask still leaves a visible placement preview. */ }
    }
    if ([3, 4].includes(o.viewportShape)) p.ellipse(x + w / 2, y + h / 2, w / 2, h / 2, 0, 0, 2 * Math.PI);
    else if (o.viewportShape === 2) p.roundRect(x, y, w, h, Math.min(w, h) * .22);
    else p.rect(x, y, w, h);
    return p;
  }
  function render(host, overlay, hostImage, showBorder) {
    const canvas = document.createElement('canvas'), image = document.createElement('img'), age = document.createElement('span');
    canvas.className = 'designer-pip-preview';canvas.setAttribute('role', 'img');
    canvas.setAttribute('aria-label', overlay.camera.name + ' picture-in-picture preview · ' + mode(overlay));
    image.alt = '';image.style.display = 'none';age.hidden = true;image._ageLabel = age;
    host.append(canvas, image, age);
    function paint() {
      const width = host.clientWidth, height = host.clientHeight;if (!width || !height) return;
      const ratio = Math.min(window.devicePixelRatio || 1, 2);
      canvas.width = Math.round(width * ratio);canvas.height = Math.round(height * ratio);
      const ctx = canvas.getContext('2d');ctx.scale(ratio, ratio);
      let b = bounds(overlay, width, height, {left:parseFloat(hostImage.style.left)||0,top:parseFloat(hostImage.style.top)||0,
        width:parseFloat(hostImage.style.width)||width,height:parseFloat(hostImage.style.height)||height});
      // Custom masks follow the host's rendered image, including stretch, zoom and pan.
      if (overlay.viewportShape === 5) {
        const sw = hostImage.naturalWidth || 1600, sh = hostImage.naturalHeight || 900;
        const scale = Math.max(1600 / sw, 900 / sh), rw = sw * scale, rh = sh * scale;
        const sx = (parseFloat(hostImage.style.width) || width) / rw, sy = (parseFloat(hostImage.style.height) || height) / rh;
        const ref = bounds(overlay, 1600, 900);
        b = {left: (parseFloat(hostImage.style.left) || 0) + (ref.left - (1600 - rw) / 2) * sx,
          top: (parseFloat(hostImage.style.top) || 0) + (ref.top - (900 - rh) / 2) * sy, width: ref.width * sx, height: ref.height * sy};
      }
      const clip = path(overlay, b);ctx.globalAlpha = clamp(overlay.viewportOpacityPercent ?? 100, 20, 100) / 100;
      ctx.save();ctx.clip(clip, overlay.viewportShape === 5 ? 'evenodd' : 'nonzero');
      ctx.fillStyle = '#182c40';ctx.fillRect(b.left, b.top, b.width, b.height);
      if (image.naturalWidth) {
        const scale = Math.max(b.width / image.naturalWidth, b.height / image.naturalHeight) * clamp(overlay.zoomPercent ?? 100, 100, 300) / 100;
        const w = b.width / scale, h = b.height / scale;
        ctx.drawImage(image, (image.naturalWidth - w) * (overlay.imageHorizontalPositionPercent ?? 50) / 100,
          (image.naturalHeight - h) * (overlay.imageVerticalPositionPercent ?? 50) / 100, w, h, b.left, b.top, b.width, b.height);
      } else {
        ctx.fillStyle = '#c8dcf0';ctx.font = '11px Segoe UI';
        ctx.fillText(overlay.camera.name, b.left + 6, b.top + 17, Math.max(1, b.width - 12));
        if (b.height > 40) ctx.fillText('Snapshot unavailable', b.left + 6, b.top + 33, Math.max(1, b.width - 12));
      }
      ctx.restore();
      if (showBorder && overlay.showBorder !== false) {ctx.strokeStyle = '#7f9bb5';ctx.lineWidth = 1;ctx.stroke(clip);}
    }
    canvas.paint = paint;image.onload = paint;image.onerror = paint;
    dashboardUX.snapshot(image, overlay.camera.slot);paint();
  }
  return {all, slot, forHost, mode, open, bounds, render};
})();
