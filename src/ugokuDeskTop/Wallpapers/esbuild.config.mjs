import { context, build } from "esbuild";
import { readdirSync, existsSync } from "fs";
import { join } from "path";

const wallpapersDir = import.meta.dirname;
const isWatch = process.argv.includes("--watch");

// src/ ディレクトリを持つ壁紙を自動検出
function discoverEntryPoints() {
  const entries = {};
  for (const dir of readdirSync(wallpapersDir, { withFileTypes: true })) {
    if (!dir.isDirectory()) continue;
    const mainTs = join(wallpapersDir, dir.name, "src", "main.ts");
    if (existsSync(mainTs)) {
      entries[`${dir.name}/main`] = mainTs;
    }
  }
  return entries;
}

const entryPoints = discoverEntryPoints();

if (Object.keys(entryPoints).length === 0) {
  console.log("No wallpaper entry points found.");
  process.exit(0);
}

console.log("Entry points:", Object.keys(entryPoints));

/** @type {import("esbuild").BuildOptions} */
const buildOptions = {
  entryPoints,
  outdir: wallpapersDir,
  bundle: true,
  format: "iife",
  target: "es2020",
  sourcemap: isWatch,
  minify: !isWatch,
};

if (isWatch) {
  // --- Dev モード: watch ---
  // ファイル変更の検知・リロードは C# 側の FileSystemWatcher が担当
  const ctx = await context({
    ...buildOptions,
    plugins: [
      {
        name: "build-notify",
        setup(build) {
          build.onEnd((result) => {
            if (result.errors.length === 0) {
              console.log(`[${new Date().toLocaleTimeString()}] Build OK`);
            }
          });
        },
      },
    ],
  });

  await ctx.watch();
  console.log("Watching for changes... (C# FileSystemWatcher handles reload)");
} else {
  // --- Production ビルド ---
  await build(buildOptions);
  console.log("Build complete.");
}
