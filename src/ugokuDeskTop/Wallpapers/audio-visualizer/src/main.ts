// ============================================================
// Audio Visualizer - メインスクリプト
// C# の WebView2 から onFrequencyData() で周波数データを受け取り
// Canvas 2D で描画する
// ============================================================

interface Particle {
  x: number;
  y: number;
  vx: number;
  vy: number;
  life: number;
  hue: number;
}

interface MonitorRegion {
  cx: number;
  cy: number;
  width: number;
  height: number;
}

/**
 * screen.width でモニター1枚の幅を推定し、
 * キャンバスが複数モニターにまたがっていれば各モニターの中心を返す。
 */
function detectMonitors(): MonitorRegion[] {
  const w = canvas.width;
  const h = canvas.height;
  const screenW = window.screen.width;
  const numMonitors = Math.max(1, Math.round(w / screenW));
  const monW = w / numMonitors;

  const monitors: MonitorRegion[] = [];
  for (let i = 0; i < numMonitors; i++) {
    monitors.push({
      cx: monW * i + monW / 2,
      cy: h / 2,
      width: monW,
      height: h,
    });
  }
  return monitors;
}

// --- Canvas セットアップ ---
const canvas = document.getElementById("canvas") as HTMLCanvasElement;
const ctx = canvas.getContext("2d")!;

function resize(): void {
  canvas.width = window.innerWidth;
  canvas.height = window.innerHeight;
}
resize();
window.addEventListener("resize", resize);

// --- 周波数データ（C# から送られてくる 64 バンド、対数マッピング）---
const BANDS = 64;
let frequencyData = new Array<number>(BANDS).fill(0);
let smoothedData = new Array<number>(BANDS).fill(0);
let peakData = new Array<number>(BANDS).fill(0);
let time = 0;
let monitorParticles: Particle[][] = [];

// ============================================================
// 周波数帯域ヘルパー
// C# 側 3帯域分割マッピング:
//   Low  bands  0-11 (12本):   30 -  200 Hz  キック・ベース
//   Mid  bands 12-53 (42本):  200 - 4000 Hz  ボーカル・スネア・ギター
//   High bands 54-63 (10本): 4000 -12000 Hz  ハイハット・シンバル
// ============================================================

/** 指定バンド範囲の平均エネルギーを返す */
function bandEnergy(from: number, to: number): number {
  let sum = 0;
  const end = Math.min(to, smoothedData.length);
  for (let i = from; i < end; i++) sum += smoothedData[i];
  return sum / (end - from);
}

/** キック / ベース (30-200 Hz): band 0-11 */
function kickEnergy(): number { return bandEnergy(0, 12); }

/** ローミッド / スネアボディ (200-600 Hz): band 12-27 */
function lowMidEnergy(): number { return bandEnergy(12, 28); }

/** ミッド (600-4000 Hz): band 28-53 — ボーカル・スネアアタック */
function midEnergy(): number { return bandEnergy(28, 54); }

/** ハイ (4000-12000 Hz): band 54-63 — ハイハット・シンバル */
function highEnergy(): number { return bandEnergy(54, 64); }

// C# からの呼び出しポイント
(window as any).onFrequencyData = (data: number[]): void => {
  frequencyData = data;
};

// --- 描画ループ ---
function draw(): void {
  const w = canvas.width;
  const h = canvas.height;
  const bands = frequencyData.length;

  // スムージング
  for (let i = 0; i < bands; i++) {
    smoothedData[i] += (frequencyData[i] - smoothedData[i]) * 0.3;
    if (smoothedData[i] > peakData[i]) {
      peakData[i] = smoothedData[i];
    } else {
      peakData[i] *= 0.98;
    }
  }

  // 全体のエネルギー
  let totalEnergy = 0;
  for (let i = 0; i < bands; i++) totalEnergy += smoothedData[i];
  totalEnergy /= bands;

  // 背景 — キック/ベースの重低音で脈動するエフェクト
  const bassImpact = kickEnergy();

  const bgHue = (time * 0.3 + totalEnergy * 100 + bassImpact * 60) % 360;
  const bgSat = 30 + bassImpact * 40;     // キックで彩度UP
  const bgLight = 3 + totalEnergy * 5 + bassImpact * 8; // キックで明度UP
  ctx.fillStyle = `hsla(${bgHue}, ${bgSat}%, ${bgLight}%, 0.25)`;
  ctx.fillRect(0, 0, w, h);

  // === 各モニターの中央に円形ビジュアライザーを描画 ===
  const monitors = detectMonitors();

  // モニター数に合わせてパーティクル配列を確保
  while (monitorParticles.length < monitors.length) {
    monitorParticles.push([]);
  }

  for (let mi = 0; mi < monitors.length; mi++) {
    const mon = monitors[mi];
    const baseRadius = Math.min(mon.width, mon.height) * 0.15;
    drawRadialBars(mon.cx, mon.cy, baseRadius, bands, bgHue, mon.width);
    drawCenterCircle(mon.cx, mon.cy, baseRadius, totalEnergy, bgHue);
    updateParticles(monitorParticles[mi], mon.cx, mon.cy, baseRadius, totalEnergy, bgHue);
  }

  drawWaveform(w, h, bands, bgHue);

  time++;
  requestAnimationFrame(draw);
}

// --- 円形バー描画 ---
function drawRadialBars(
  cx: number,
  cy: number,
  baseRadius: number,
  bands: number,
  bgHue: number,
  canvasWidth: number,
): void {
  for (let i = 0; i < bands; i++) {
    const angle = (i / bands) * Math.PI * 2 - Math.PI / 2;
    const value = smoothedData[i];
    const peak = peakData[i];
    const barLength = value * baseRadius * 2.5;
    const peakPos = peak * baseRadius * 2.5;

    const x1 = cx + Math.cos(angle) * baseRadius;
    const y1 = cy + Math.sin(angle) * baseRadius;
    const x2 = cx + Math.cos(angle) * (baseRadius + barLength);
    const y2 = cy + Math.sin(angle) * (baseRadius + barLength);

    // バー
    const hue = ((i / bands) * 300 + time * 0.5) % 360;
    const lineWidth = Math.max(2, (canvasWidth / bands) * 0.4);
    ctx.beginPath();
    ctx.moveTo(x1, y1);
    ctx.lineTo(x2, y2);
    ctx.strokeStyle = `hsla(${hue}, 85%, ${50 + value * 30}%, ${0.7 + value * 0.3})`;
    ctx.lineWidth = lineWidth;
    ctx.lineCap = "round";
    ctx.stroke();

    // ピークドット
    const px = cx + Math.cos(angle) * (baseRadius + peakPos + 5);
    const py = cy + Math.sin(angle) * (baseRadius + peakPos + 5);
    ctx.beginPath();
    ctx.arc(px, py, 2, 0, Math.PI * 2);
    ctx.fillStyle = `hsla(${hue}, 90%, 70%, ${peak})`;
    ctx.fill();

    // 内側ミラー（小さめ）
    const innerLen = value * baseRadius * 0.8;
    const ix = cx - Math.cos(angle) * (baseRadius * 0.3 + innerLen);
    const iy = cy - Math.sin(angle) * (baseRadius * 0.3 + innerLen);
    ctx.beginPath();
    ctx.moveTo(
      cx - Math.cos(angle) * baseRadius * 0.3,
      cy - Math.sin(angle) * baseRadius * 0.3,
    );
    ctx.lineTo(ix, iy);
    ctx.strokeStyle = `hsla(${hue}, 70%, 40%, ${0.3 + value * 0.3})`;
    ctx.lineWidth = lineWidth * 0.6;
    ctx.stroke();
  }
}

// --- 中央の円（呼吸エフェクト）---
function drawCenterCircle(
  cx: number,
  cy: number,
  baseRadius: number,
  totalEnergy: number,
  bgHue: number,
): void {
  const breathe = baseRadius * 0.3 + totalEnergy * baseRadius * 0.5;
  const gradient = ctx.createRadialGradient(cx, cy, 0, cx, cy, breathe);
  gradient.addColorStop(0, `hsla(${bgHue + 180}, 60%, 30%, 0.6)`);
  gradient.addColorStop(0.7, `hsla(${bgHue + 180}, 60%, 20%, 0.2)`);
  gradient.addColorStop(1, "transparent");
  ctx.beginPath();
  ctx.arc(cx, cy, breathe, 0, Math.PI * 2);
  ctx.fillStyle = gradient;
  ctx.fill();
}

// --- 下部の波形 ---
function drawWaveform(
  w: number,
  h: number,
  bands: number,
  bgHue: number,
): void {
  const waveY = h * 0.85;
  const waveHeight = h * 0.1;

  ctx.beginPath();
  ctx.moveTo(0, waveY);
  for (let x = 0; x <= w; x += 3) {
    const bandIdx = Math.floor((x / w) * bands);
    const value = smoothedData[Math.min(bandIdx, bands - 1)];
    const wave = Math.sin(x * 0.02 + time * 0.05) * 10;
    const y = waveY - value * waveHeight + wave;
    ctx.lineTo(x, y);
  }
  ctx.lineTo(w, h);
  ctx.lineTo(0, h);
  ctx.closePath();

  const waveGrad = ctx.createLinearGradient(0, waveY - waveHeight, 0, h);
  waveGrad.addColorStop(0, `hsla(${bgHue + 90}, 70%, 50%, 0.4)`);
  waveGrad.addColorStop(1, `hsla(${bgHue + 90}, 70%, 20%, 0.1)`);
  ctx.fillStyle = waveGrad;
  ctx.fill();
}

// --- パーティクル（モニター単位）---
function updateParticles(
  particles: Particle[],
  cx: number,
  cy: number,
  baseRadius: number,
  totalEnergy: number,
  bgHue: number,
): void {
  if (Math.random() < totalEnergy * 0.5) {
    particles.push({
      x: cx + (Math.random() - 0.5) * baseRadius * 2,
      y: cy + (Math.random() - 0.5) * baseRadius * 2,
      vx: (Math.random() - 0.5) * totalEnergy * 4,
      vy: (Math.random() - 0.5) * totalEnergy * 4,
      life: 1,
      hue: (bgHue + Math.random() * 60) % 360,
    });
  }

  for (let i = particles.length - 1; i >= 0; i--) {
    const p = particles[i];
    p.x += p.vx;
    p.y += p.vy;
    p.life -= 0.015;

    if (p.life <= 0) {
      particles.splice(i, 1);
      continue;
    }

    ctx.beginPath();
    ctx.arc(p.x, p.y, p.life * 3, 0, Math.PI * 2);
    ctx.fillStyle = `hsla(${p.hue}, 80%, 60%, ${p.life * 0.5})`;
    ctx.fill();
  }

  // パーティクル数制限
  while (particles.length > 200) particles.shift();
}

// --- 開始 ---
draw();
