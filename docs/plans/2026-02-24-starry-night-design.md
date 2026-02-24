# starry-night 壁紙 実装計画

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** GPUをほぼ使わない軽量な星空壁紙を CSS アニメーション + 最小限 JS で実装する

**Architecture:** 星は DOM 要素として JS で初期化時にランダム配置し、瞬きは CSS @keyframes で制御。流れ星は setTimeout でランダムスケジューリングし、CSS アニメーションで斜め移動 + フェードアウト。Canvas / WebGL / requestAnimationFrame は一切使わない。

**Tech Stack:** HTML, CSS (animations), TypeScript (esbuild でバンドル)

---

## Task 1: index.html を作成（HTML構造 + CSS）

**Files:**
- Create: `src/ugokuDeskTop/Wallpapers/starry-night/index.html`

**Step 1: index.html を作成**

既存壁紙 (audio-visualizer) と同じパターンで、`<script src="main.js">` で TypeScript ビルド成果物を読み込む構造。

```html
<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8">
    <style>
        * { margin: 0; padding: 0; overflow: hidden; }
        body {
            width: 100vw;
            height: 100vh;
            background: linear-gradient(to bottom, #050510 0%, #0a1628 50%, #121d3d 100%);
        }

        /* 星コンテナ */
        #stars { position: fixed; inset: 0; }

        /* 星の基本スタイル */
        .star {
            position: absolute;
            border-radius: 50%;
            background: #fff;
            will-change: opacity;
            animation: twinkle var(--duration) ease-in-out infinite;
            animation-delay: var(--delay);
            width: var(--size);
            height: var(--size);
            opacity: var(--base-opacity);
        }

        @keyframes twinkle {
            0%, 100% { opacity: var(--base-opacity); }
            50% { opacity: var(--min-opacity); }
        }

        /* 流れ星 */
        .shooting-star {
            position: absolute;
            width: 2px;
            height: 2px;
            background: #fff;
            border-radius: 50%;
            opacity: 0;
            will-change: transform, opacity;
        }

        .shooting-star.active {
            animation: shoot var(--shoot-duration) linear forwards;
        }

        @keyframes shoot {
            0% {
                opacity: 1;
                transform: translate(0, 0) scale(1);
                box-shadow: 0 0 4px 1px rgba(255,255,255,0.6),
                            -30px 0 20px 2px rgba(180,200,255,0.3),
                            -60px 0 40px 1px rgba(180,200,255,0.1);
            }
            70% {
                opacity: 0.8;
                transform: translate(var(--dx), var(--dy)) scale(0.8);
                box-shadow: 0 0 2px 1px rgba(255,255,255,0.4),
                            -20px 0 15px 1px rgba(180,200,255,0.2);
            }
            100% {
                opacity: 0;
                transform: translate(calc(var(--dx) * 1.4), calc(var(--dy) * 1.4)) scale(0.3);
                box-shadow: none;
            }
        }
    </style>
</head>
<body>
    <div id="stars"></div>
    <script src="main.js"></script>
</body>
</html>
```

**Step 2: ビルドできることを確認**

esbuild は `src/main.ts` を自動検出するので、Task 2 で main.ts を作った後にまとめて確認する。

---

## Task 2: main.ts を作成（星の生成 + 流れ星スケジューリング）

**Files:**
- Create: `src/ugokuDeskTop/Wallpapers/starry-night/src/main.ts`

**Step 1: main.ts を作成**

```typescript
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

// --- ユーティリティ ---
function rand(min: number, max: number): number {
  return Math.random() * (max - min) + min;
}

// --- 星の生成 ---
const layers: StarLayer[] = [
  { count: 100, sizeMin: 0.8, sizeMax: 1.2, opacityMin: 0.3, opacityMax: 0.6, durationMin: 4, durationMax: 8 },
  { count: 50,  sizeMin: 1.5, sizeMax: 2.0, opacityMin: 0.5, opacityMax: 0.8, durationMin: 2, durationMax: 5 },
  { count: 15,  sizeMin: 2.0, sizeMax: 3.0, opacityMin: 0.7, opacityMax: 1.0, durationMin: 1, durationMax: 3 },
];

for (const layer of layers) {
  for (let i = 0; i < layer.count; i++) {
    const el = document.createElement("div");
    el.className = "star";

    const size = rand(layer.sizeMin, layer.sizeMax);
    const baseOpacity = rand(layer.opacityMin, layer.opacityMax);
    const duration = rand(layer.durationMin, layer.durationMax);
    const delay = rand(0, duration); // 全星が同時に点滅しないようにずらす

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

  // 画面上部のランダムな位置から出現
  const startX = rand(10, 90);
  const startY = rand(5, 40);
  el.style.left = `${startX}%`;
  el.style.top = `${startY}%`;

  // 移動距離と方向（右下方向）
  const distance = rand(150, 350);
  const angle = rand(20, 50) * (Math.PI / 180); // 20-50度
  const dx = Math.cos(angle) * distance;
  const dy = Math.sin(angle) * distance;

  el.style.setProperty("--dx", `${dx}px`);
  el.style.setProperty("--dy", `${dy}px`);
  el.style.setProperty("--shoot-duration", `${rand(0.6, 1.2)}s`);

  container.appendChild(el);

  // アニメーション開始（次フレームで class 付与）
  requestAnimationFrame(() => el.classList.add("active"));

  // アニメーション後に DOM から削除
  el.addEventListener("animationend", () => el.remove());
}

function scheduleShootingStar(): void {
  const interval = rand(10, 30) * 1000; // 10-30秒
  setTimeout(() => {
    spawnShootingStar();
    scheduleShootingStar();
  }, interval);
}

// 初回は 3-8秒後に開始
setTimeout(scheduleShootingStar, rand(3, 8) * 1000);
```

**Step 2: esbuild でビルド**

Run: `cd src/ugokuDeskTop/Wallpapers && npm run build`
Expected: `Entry points: [... "starry-night/main" ...]` を含む出力で Build complete.

**Step 3: コミット**

```bash
git add src/ugokuDeskTop/Wallpapers/starry-night/
git commit -m "feat: add starry-night wallpaper (lightweight CSS-only stars)"
```

---

## Task 3: 動作確認 + 微調整

**Step 1: アプリを起動して壁紙を確認**

Run: `dotnet run --project src/ugokuDeskTop/ugokuDeskTop.csproj`

トレイアイコンから starry-night を選択し、以下を確認:
- 深い紺色のグラデーション背景が表示される
- 3レイヤーの星が自然に瞬いている
- 10-30秒間隔で流れ星が出現・消滅する
- デスクトップアイコンが見やすい

**Step 2: 必要に応じて微調整**

星の密度、瞬き速度、流れ星の頻度などを調整。

**Step 3: 最終コミット**

```bash
git add -A
git commit -m "feat: starry-night wallpaper - tuning"
```
