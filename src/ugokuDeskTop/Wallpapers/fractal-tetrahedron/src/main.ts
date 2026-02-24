// ============================================================
// Fractal Tetrahedron - メインスクリプト
// WebGL2 レイマーチングで Sierpinski Tetrahedron を描画
// ============================================================

import vertexSource from './shaders/fullscreen-quad.vert';
import fragmentSource from './shaders/fractal-tetrahedron.frag';

// --- Canvas & WebGL2 セットアップ ---
const canvas = document.getElementById('canvas') as HTMLCanvasElement;
const gl = canvas.getContext('webgl2');
if (!gl) {
  throw new Error('WebGL2 is not supported');
}

function resize(): void {
  canvas.width = window.innerWidth;
  canvas.height = window.innerHeight;
  gl!.viewport(0, 0, canvas.width, canvas.height);
}
resize();
window.addEventListener('resize', resize);

// --- シェーダーコンパイル ---
function createShader(type: number, source: string): WebGLShader {
  const shader = gl!.createShader(type)!;
  gl!.shaderSource(shader, source);
  gl!.compileShader(shader);
  if (!gl!.getShaderParameter(shader, gl!.COMPILE_STATUS)) {
    const log = gl!.getShaderInfoLog(shader);
    gl!.deleteShader(shader);
    throw new Error(`Shader compile error: ${log}`);
  }
  return shader;
}

function createProgram(vertSrc: string, fragSrc: string): WebGLProgram {
  const vert = createShader(gl!.VERTEX_SHADER, vertSrc);
  const frag = createShader(gl!.FRAGMENT_SHADER, fragSrc);
  const program = gl!.createProgram()!;
  gl!.attachShader(program, vert);
  gl!.attachShader(program, frag);
  gl!.linkProgram(program);
  if (!gl!.getProgramParameter(program, gl!.LINK_STATUS)) {
    throw new Error(`Program link error: ${gl!.getProgramInfoLog(program)}`);
  }
  return program;
}

const program = createProgram(vertexSource, fragmentSource);
gl.useProgram(program);

// --- フルスクリーン四角形 (triangle strip) ---
const vertices = new Float32Array([-1, -1, 1, -1, -1, 1, 1, 1]);
const buffer = gl.createBuffer();
gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
gl.bufferData(gl.ARRAY_BUFFER, vertices, gl.STATIC_DRAW);

const positionLoc = gl.getAttribLocation(program, 'position');
gl.enableVertexAttribArray(positionLoc);
gl.vertexAttribPointer(positionLoc, 2, gl.FLOAT, false, 0, 0);

// --- Uniform ロケーション ---
const timeLoc = gl.getUniformLocation(program, 'iTime');
const resolutionLoc = gl.getUniformLocation(program, 'iResolution');

// --- レンダーループ ---
const startTime = performance.now();

function render(): void {
  const time = (performance.now() - startTime) / 1000.0;
  gl!.uniform1f(timeLoc, time);
  gl!.uniform2f(resolutionLoc, canvas.width, canvas.height);
  gl!.drawArrays(gl!.TRIANGLE_STRIP, 0, 4);
  requestAnimationFrame(render);
}

render();
