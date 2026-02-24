// ============================================================
// Starry Night - 軽量星空壁紙
// CSS アニメーションのみ。Canvas / WebGL / rAF 不使用。
// ============================================================

interface StarLayer {
  count: number;
  sizeMin: number;
  sizeMax: number;
  opacityMin: number;
  opacityMax: number;
  durationMin: number;
  durationMax: number;
}

const container = document.getElementById("stars")!;

function rand(min: number, max: number): number {
  return Math.random() * (max - min) + min;
}

// --- 星の生成 ---
const layers: StarLayer[] = [
  { count: 150, sizeMin: 0.5, sizeMax: 1.2, opacityMin: 0.3, opacityMax: 0.6, durationMin: 4, durationMax: 8 },
  { count: 50,  sizeMin: 1.5, sizeMax: 2.0, opacityMin: 0.5, opacityMax: 0.8, durationMin: 2, durationMax: 5 },
];

for (const layer of layers) {
  for (let i = 0; i < layer.count; i++) {
    const el = document.createElement("div");
    el.className = "star";

    const size = rand(layer.sizeMin, layer.sizeMax);
    const baseOpacity = rand(layer.opacityMin, layer.opacityMax);
    const duration = rand(layer.durationMin, layer.durationMax);
    const delay = rand(0, duration);

    el.style.left = `${rand(0, 100)}%`;
    el.style.top = `${rand(0, 100)}%`;
    el.style.setProperty("--size", `${size}px`);
    el.style.setProperty("--base-opacity", `${baseOpacity}`);
    el.style.setProperty("--min-opacity", `${baseOpacity * 0.2}`);
    el.style.setProperty("--duration", `${duration}s`);
    el.style.setProperty("--delay", `-${delay}s`);

    container.appendChild(el);
  }
}

// --- 流れ星 ---
function spawnShootingStar(): void {
  const el = document.createElement("div");
  el.className = "shooting-star";

  const startX = rand(10, 90);
  const startY = rand(5, 40);
  el.style.left = `${startX}%`;
  el.style.top = `${startY}%`;

  const distance = rand(150, 350);
  const angle = rand(20, 50) * (Math.PI / 180);
  const dx = Math.cos(angle) * distance;
  const dy = Math.sin(angle) * distance;

  el.style.setProperty("--dx", `${dx}px`);
  el.style.setProperty("--dy", `${dy}px`);
  el.style.setProperty("--shoot-duration", `${rand(0.6, 1.2)}s`);

  container.appendChild(el);

  requestAnimationFrame(() => el.classList.add("active"));

  el.addEventListener("animationend", () => el.remove());
}

function scheduleShootingStar(): void {
  const interval = rand(10, 30) * 1000;
  setTimeout(() => {
    spawnShootingStar();
    scheduleShootingStar();
  }, interval);
}

setTimeout(scheduleShootingStar, rand(3, 8) * 1000);
