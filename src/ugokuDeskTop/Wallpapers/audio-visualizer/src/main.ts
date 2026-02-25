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

// --- デスクトップ状態（C# から受信）---
let desktopVisible = false;
let mouseNormX = 0.5; // 0=画面左端, 1=右端
let mouseNormY = 0.5; // 0=画面上端, 1=下端

// --- APO 状態（C# から受信）---
let apoActive = false;
let apoFilterType = 'OFF'; // 'LP' | 'HP' | 'BP' | 'NO' | 'PK' | 'LSC' | 'HSC' | 'OFF'
let apoFreqHz = 0;
let apoQ = 0;
let apoGainDb = 0;

// C# からの呼び出しポイント
(window as any).onFrequencyData = (data: number[]): void => {
  frequencyData = data;
};

(window as any).onDesktopState = (
  visible: boolean,
  mx: number,
  my: number,
): void => {
  desktopVisible = visible;
  mouseNormX = mx;
  mouseNormY = my;
};

(window as any).onApoState = (
  active: boolean,
  filterType: string,
  freqHz: number,
  q: number,
  gainDb: number,
): void => {
  apoActive = active;
  apoFilterType = filterType;
  apoFreqHz = freqHz;
  apoQ = q;
  apoGainDb = gainDb || 0;
};

/**
 * バンドインデックスから周波数(Hz)を逆算する。
 * AudioCaptureService の3帯域対数マッピングに対応。
 */
function bandToFrequency(band: number): number {
  if (band < 12) {
    const t = (band + 0.5) / 12;
    return 30 * Math.pow(200 / 30, t);
  } else if (band < 54) {
    const t = (band - 12 + 0.5) / 42;
    return 200 * Math.pow(4000 / 200, t);
  } else {
    const t = (band - 54 + 0.5) / 10;
    return 4000 * Math.pow(12000 / 4000, t);
  }
}

/**
 * デスクトップ表示中に、マウスX位置に応じて特定バンドをブーストする EQ 倍率を返す。
 * APO 連携が有効な場合は、実際のローパスフィルター応答を模擬した減衰を返す。
 */
function eqMultiplier(bandIndex: number, totalBands: number): number {
  if (apoActive) {
    const freq = bandToFrequency(bandIndex);
    const ratio = freq / apoFreqHz;

    switch (apoFilterType) {
      case 'LP':
        // ローパス: カットオフ以上を減衰
        if (ratio <= 1) return 1;
        return 1 / (1 + Math.pow(ratio, apoQ * 2));

      case 'HP':
        // ハイパス: カットオフ以下を減衰
        if (ratio >= 1) return 1;
        return 1 / (1 + Math.pow(1 / ratio, apoQ * 2));

      case 'BP': {
        // バンドパス: 中心周波数付近のみ通す
        const logDist = Math.abs(Math.log2(ratio));
        const bw = 1 / apoQ; // Q が高いほど帯域が狭い
        return Math.exp(-logDist * logDist / (bw * bw) * 2);
      }

      case 'NO': {
        // ノッチ: 中心周波数付近を除去
        const logDist2 = Math.abs(Math.log2(ratio));
        const bw2 = 1 / apoQ;
        const notch = Math.exp(-logDist2 * logDist2 / (bw2 * bw2) * 2);
        return 1 - notch * 0.9; // 完全には消さない（視覚的に）
      }

      case 'PK': {
        // ピーキングEQ: 中心周波数付近をブースト/カット
        const logDist3 = Math.abs(Math.log2(ratio));
        const bw3 = 1 / apoQ;
        const shape = Math.exp(-logDist3 * logDist3 / (bw3 * bw3) * 2);
        const gainLinear = Math.pow(10, apoGainDb / 20);
        return 1 + (gainLinear - 1) * shape;
      }

      case 'LSC': {
        // ローシェルフ: コーナー周波数以下をブースト/カット
        const gainLinear2 = Math.pow(10, apoGainDb / 20);
        if (ratio <= 0.5) return gainLinear2;
        if (ratio >= 2) return 1;
        // トランジション
        const t = (Math.log2(ratio) + 1) / 2; // 0..1
        return gainLinear2 + (1 - gainLinear2) * t;
      }

      case 'HSC': {
        // ハイシェルフ: コーナー周波数以上をブースト/カット
        const gainLinear3 = Math.pow(10, apoGainDb / 20);
        if (ratio >= 2) return gainLinear3;
        if (ratio <= 0.5) return 1;
        const t2 = (Math.log2(ratio) + 1) / 2;
        return 1 + (gainLinear3 - 1) * t2;
      }

      default:
        return 1;
    }
  }

  // 通常モード: マウスベースの視覚EQ
  const eqCenter = mouseNormX * totalBands;
  const eqWidth = 10;
  const dist = Math.abs(bandIndex - eqCenter);
  const boost = Math.max(0, 1 - dist / eqWidth);
  const intensity = (1 - mouseNormY) * 3;
  return 1 + boost * intensity;
}

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
  drawApoOverlay(w, h);

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
    const eq = eqMultiplier(i, bands);
    const value = Math.min(smoothedData[i] * eq, 1);
    const peak = Math.min(peakData[i] * eq, 1);
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
    const bandIdx = Math.min(Math.floor((x / w) * bands), bands - 1);
    const eq = eqMultiplier(bandIdx, bands);
    const value = Math.min(smoothedData[bandIdx] * eq, 1);
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

// --- APO フィルター状態オーバーレイ ---
const filterLabels: Record<string, string> = {
  LP: 'LOW PASS',
  HP: 'HIGH PASS',
  BP: 'BAND PASS',
  NO: 'NOTCH',
  PK: 'PEAKING EQ',
  LSC: 'LOW SHELF',
  HSC: 'HIGH SHELF',
};

function drawApoOverlay(w: number, h: number): void {
  if (!apoActive) return;

  const overlayX = w * 0.35;
  const overlayY = h * 0.04;
  const barWidth = w * 0.3;

  // 背景パネル
  ctx.save();
  ctx.beginPath();
  ctx.roundRect(overlayX - 12, overlayY - 8, barWidth + 24, 56, 8);
  ctx.fillStyle = 'rgba(0, 0, 0, 0.5)';
  ctx.fill();

  // フィルター情報テキスト
  ctx.font = '13px monospace';
  ctx.fillStyle = 'rgba(255, 200, 100, 0.9)';
  ctx.textAlign = 'left';

  const label = filterLabels[apoFilterType] || apoFilterType;
  const freqStr =
    apoFreqHz >= 1000
      ? `${(apoFreqHz / 1000).toFixed(1)}kHz`
      : `${apoFreqHz.toFixed(0)}Hz`;

  // フィルタータイプに応じてパラメータ表示を変える
  let paramStr: string;
  if (apoFilterType === 'PK' || apoFilterType === 'LSC' || apoFilterType === 'HSC') {
    const sign = apoGainDb >= 0 ? '+' : '';
    paramStr = `${label}  Fc: ${freqStr}  ${sign}${apoGainDb.toFixed(1)}dB`;
  } else {
    paramStr = `${label}  Fc: ${freqStr}  Q: ${apoQ.toFixed(1)}`;
  }
  ctx.fillText(paramStr, overlayX, overlayY + 14);

  // 周波数位置バー
  const barY = overlayY + 28;
  ctx.fillStyle = 'rgba(255, 255, 255, 0.1)';
  ctx.fillRect(overlayX, barY, barWidth, 6);

  // マーカー（周波数の位置）
  const freqNorm = Math.log(apoFreqHz / 80) / Math.log(16000 / 80);
  const markerX = overlayX + Math.max(0, Math.min(1, freqNorm)) * barWidth;

  // フィルタータイプに応じた影響範囲の描画
  switch (apoFilterType) {
    case 'LP':
      // ローパス: マーカーより右がカット
      ctx.fillStyle = 'rgba(255, 100, 50, 0.3)';
      ctx.fillRect(markerX, barY, overlayX + barWidth - markerX, 6);
      break;
    case 'HP':
      // ハイパス: マーカーより左がカット
      ctx.fillStyle = 'rgba(255, 100, 50, 0.3)';
      ctx.fillRect(overlayX, barY, markerX - overlayX, 6);
      break;
    case 'BP': {
      // バンドパス: マーカー周辺を通す（それ以外をカット）
      const bpWidth = barWidth * (0.5 / apoQ);
      ctx.fillStyle = 'rgba(255, 100, 50, 0.3)';
      ctx.fillRect(overlayX, barY, Math.max(0, markerX - bpWidth / 2 - overlayX), 6);
      ctx.fillRect(markerX + bpWidth / 2, barY, overlayX + barWidth - markerX - bpWidth / 2, 6);
      // 通過帯域
      ctx.fillStyle = 'rgba(100, 255, 150, 0.3)';
      ctx.fillRect(Math.max(overlayX, markerX - bpWidth / 2), barY, bpWidth, 6);
      break;
    }
    case 'NO': {
      // ノッチ: マーカー周辺を除去
      const noWidth = barWidth * (0.3 / apoQ);
      ctx.fillStyle = 'rgba(255, 100, 50, 0.4)';
      ctx.fillRect(Math.max(overlayX, markerX - noWidth / 2), barY, noWidth, 6);
      break;
    }
    case 'PK': {
      // ピーキング: ブースト(緑)/カット(赤)
      const pkWidth = barWidth * (0.4 / apoQ);
      const color = apoGainDb >= 0 ? 'rgba(100, 255, 150, 0.4)' : 'rgba(255, 100, 50, 0.4)';
      ctx.fillStyle = color;
      ctx.fillRect(Math.max(overlayX, markerX - pkWidth / 2), barY, pkWidth, 6);
      break;
    }
    case 'LSC':
      // ローシェルフ: マーカーより左を増減
      ctx.fillStyle = apoGainDb >= 0 ? 'rgba(100, 255, 150, 0.3)' : 'rgba(255, 100, 50, 0.3)';
      ctx.fillRect(overlayX, barY, markerX - overlayX, 6);
      break;
    case 'HSC':
      // ハイシェルフ: マーカーより右を増減
      ctx.fillStyle = apoGainDb >= 0 ? 'rgba(100, 255, 150, 0.3)' : 'rgba(255, 100, 50, 0.3)';
      ctx.fillRect(markerX, barY, overlayX + barWidth - markerX, 6);
      break;
  }

  // マーカードット
  ctx.beginPath();
  ctx.arc(markerX, barY + 3, 5, 0, Math.PI * 2);
  ctx.fillStyle = 'rgba(255, 200, 100, 0.9)';
  ctx.fill();

  // 周波数ラベル（バーの下）
  ctx.font = '10px monospace';
  ctx.fillStyle = 'rgba(255, 255, 255, 0.4)';
  ctx.textAlign = 'center';
  ctx.fillText('80', overlayX, barY + 18);
  ctx.fillText('1k', overlayX + barWidth * 0.5, barY + 18);
  ctx.fillText('16k', overlayX + barWidth, barY + 18);

  ctx.restore();
}

// --- 開始 ---
draw();
