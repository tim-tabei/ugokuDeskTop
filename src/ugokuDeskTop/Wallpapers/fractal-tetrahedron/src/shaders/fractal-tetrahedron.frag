#version 300 es
precision highp float;

uniform float iTime;
uniform vec2 iResolution;

out vec4 fragColor;

// --- 正四面体の 4 頂点 ---
const vec3 va = vec3( 1.0,  1.0,  1.0);
const vec3 vb = vec3(-1.0, -1.0,  1.0);
const vec3 vc = vec3(-1.0,  1.0, -1.0);
const vec3 vd = vec3( 1.0, -1.0, -1.0);

// --- Sierpinski Tetrahedron SDF (IFS) ---
float sierpinskiTetrahedron(vec3 p) {
    const int ITERATIONS = 10;
    const float SCALE = 2.0;
    float s = 1.0;

    for (int i = 0; i < ITERATIONS; i++) {
        // 最も近い頂点を見つける
        vec3 closest = va;
        float minDist = dot(p - va, p - va);

        float d = dot(p - vb, p - vb);
        if (d < minDist) { minDist = d; closest = vb; }

        d = dot(p - vc, p - vc);
        if (d < minDist) { minDist = d; closest = vc; }

        d = dot(p - vd, p - vd);
        if (d < minDist) { minDist = d; closest = vd; }

        // スケーリング + 最近接頂点に向かって折り畳む
        p = SCALE * p - closest * (SCALE - 1.0);
        s *= SCALE;
    }

    return (length(p) - 1.5) / s;
}

// --- 複合回転行列 (3軸異速度) ---
mat3 rotationMatrix(float t) {
    float a1 = t * 0.15; // Y 軸: ゆっくり主回転
    float a2 = t * 0.11; // X 軸: さらに遅い傾き
    float a3 = t * 0.07; // Z 軸: 微かなロール

    float s1 = sin(a1), c1 = cos(a1);
    float s2 = sin(a2), c2 = cos(a2);
    float s3 = sin(a3), c3 = cos(a3);

    mat3 ry = mat3(
         c1, 0.0,  s1,
        0.0, 1.0, 0.0,
        -s1, 0.0,  c1
    );
    mat3 rx = mat3(
        1.0, 0.0, 0.0,
        0.0,  c2, -s2,
        0.0,  s2,  c2
    );
    mat3 rz = mat3(
         c3, -s3, 0.0,
         s3,  c3, 0.0,
        0.0, 0.0, 1.0
    );

    return ry * rx * rz;
}

// --- シーンの距離関数 ---
float map(vec3 p) {
    p = rotationMatrix(iTime) * p;
    return sierpinskiTetrahedron(p);
}

// --- レイマーチング ---
float rayMarch(vec3 ro, vec3 rd) {
    float t = 0.0;
    for (int i = 0; i < 128; i++) {
        vec3 p = ro + rd * t;
        float d = map(p);
        if (d < 0.0005) break;
        t += d;
        if (t > 20.0) break;
    }
    return t;
}

// --- 法線計算 (中央差分) ---
vec3 calcNormal(vec3 p) {
    vec2 e = vec2(0.0005, 0.0);
    return normalize(vec3(
        map(p + e.xyy) - map(p - e.xyy),
        map(p + e.yxy) - map(p - e.yxy),
        map(p + e.yyx) - map(p - e.yyx)
    ));
}

// --- AO (Ambient Occlusion) ---
float calcAO(vec3 p, vec3 n) {
    float ao = 0.0;
    float s = 1.0;
    for (int i = 1; i <= 5; i++) {
        float d = 0.02 * float(i);
        ao += s * (d - map(p + n * d));
        s *= 0.5;
    }
    return clamp(1.0 - 3.0 * ao, 0.0, 1.0);
}

void main() {
    vec2 uv = (gl_FragCoord.xy - 0.5 * iResolution.xy) / iResolution.y;

    // カメラ設定
    vec3 ro = vec3(0.0, 0.0, 4.5);
    vec3 rd = normalize(vec3(uv, -1.5));

    float t = rayMarch(ro, rd);
    vec3 col = vec3(0.0);

    if (t < 20.0) {
        vec3 p = ro + rd * t;
        vec3 n = calcNormal(p);

        // ライティング
        vec3 lightDir = normalize(vec3(0.5, 1.0, 0.8));
        vec3 lightDir2 = normalize(vec3(-0.6, -0.3, -0.5));

        // ディフューズ
        float diff = max(dot(n, lightDir), 0.0);
        float diff2 = max(dot(n, lightDir2), 0.0);

        // スペキュラー
        vec3 viewDir = normalize(ro - p);
        vec3 halfDir = normalize(lightDir + viewDir);
        float spec = pow(max(dot(n, halfDir), 0.0), 64.0);

        // アンビエントオクルージョン
        float ao = calcAO(p, n);

        // モノクロ ライティング合成
        float ambient = 0.08;
        float lighting = ambient + diff * 0.7 + diff2 * 0.15;
        lighting *= ao;
        lighting += spec * 0.35;

        // 距離によるフォグ
        float fog = exp(-0.04 * t * t);
        col = vec3(lighting * fog);

        // 微かなリムライト
        float rim = 1.0 - max(dot(n, viewDir), 0.0);
        rim = pow(rim, 3.0);
        col += vec3(rim * 0.12 * ao);
    } else {
        // 背景: 暗いグラデーション + 微かなグロー
        float vignette = length(uv) * 0.5;
        col = vec3(0.015) * (1.0 - vignette);
        col += vec3(0.03) * exp(-length(uv) * 4.0);
    }

    // ガンマ補正
    col = pow(col, vec3(1.0 / 2.2));

    // 壁紙向け暗め調整
    col *= 0.8;

    fragColor = vec4(col, 1.0);
}
