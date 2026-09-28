// NINA Advanced API 읽기 전용 점검 스크립트
//
// 장비를 움직이거나 설정을 바꾸는 요청은 하나도 보내지 않는다.
// 조회 전용 엔드포인트만 호출하고, 응답을 api-check-results/ 폴더에 저장한다.
//
// 사용법 (NINA 실행 + Advanced API 활성화 상태에서):
//   node tools/api-check.mjs                 조회 점검만
//   node tools/api-check.mjs --listen 120    조회 점검 후 120초 동안 이벤트(WebSocket) 기록
//   node tools/api-check.mjs --host 192.168.0.10 --port 1888

import { mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";

const args = process.argv.slice(2);
const opt = (name, def) => {
  const i = args.indexOf(`--${name}`);
  return i >= 0 && args[i + 1] ? args[i + 1] : def;
};
const host = opt("host", "localhost");
const port = opt("port", "1888");
const listenSec = Number(opt("listen", "0"));
const base = `http://${host}:${port}/v2/api`;
const outDir = join(process.cwd(), "api-check-results");
mkdirSync(outDir, { recursive: true });

const devices = ["camera", "mount", "focuser", "filterwheel", "guider", "switch", "weather", "rotator", "flatdevice", "safetymonitor", "dome"];

// 조회 전용 엔드포인트만. 여기에 slew/connect/set 같은 동작 엔드포인트를 넣지 말 것.
const readOnly = [
  "/version",
  "/version/nina",
  "/application-start",
  "/plugin/settings",
  "/application/plugins",
  "/equipment/info",
  ...devices.flatMap((d) => [`/equipment/${d}/info`, `/equipment/${d}/list-devices`]),
  "/equipment/focuser/last-af",
  "/equipment/guider/graph",
  "/profile/show?active=true",
  "/profile/horizon",
  "/framing/info",
  "/sequence/list-available",
  "/sequence/state",
  "/event-history",
  "/flats/status",
  "/application/logs?lineCount=100&level=INFO",
];

const fileName = (path) => path.replace(/^\//, "").replace(/[/?=&]/g, "_") + ".json";

const summary = [];
for (const path of readOnly) {
  const started = Date.now();
  let row = { path, ok: false, status: null, ms: 0, note: "" };
  try {
    const res = await fetch(base + path, { signal: AbortSignal.timeout(10000) });
    row.status = res.status;
    const text = await res.text();
    let body;
    try { body = JSON.parse(text); } catch { body = text; }
    row.ok = res.ok && (typeof body !== "object" || body.Success !== false);
    if (typeof body === "object" && body && body.Error) row.note = String(body.Error).slice(0, 120);
    writeFileSync(join(outDir, fileName(path)), JSON.stringify(body, null, 2));
  } catch (e) {
    row.note = e.name === "TimeoutError" ? "시간 초과" : String(e.cause?.code || e.message);
  }
  row.ms = Date.now() - started;
  summary.push(row);
  console.log(`${row.ok ? "OK  " : "FAIL"} ${String(row.status ?? "-").padEnd(4)} ${path}${row.note ? "  — " + row.note : ""}`);
  if (path === "/version" && !row.ok) {
    console.log(`\nNINA API에 연결할 수 없어요 (${base}). NINA 실행 여부와 Advanced API 활성화·포트를 확인하세요.`);
    process.exit(1);
  }
}

if (listenSec > 0) {
  console.log(`\n${listenSec}초 동안 이벤트를 기록해요. 이 시간 동안 NINA에서 장비 연결·해제 등을 해보세요.`);
  const events = [];
  const ws = new WebSocket(`ws://${host}:${port}/v2/socket`);
  ws.onmessage = (m) => {
    let data;
    try { data = JSON.parse(m.data); } catch { data = m.data; }
    events.push({ at: new Date().toISOString(), data });
    const name = data?.Response?.Event ?? "(message)";
    console.log(`  event ${name}`);
  };
  ws.onerror = () => console.log("  WebSocket 연결 실패");
  await new Promise((r) => setTimeout(r, listenSec * 1000));
  ws.close();
  writeFileSync(join(outDir, "_events.json"), JSON.stringify(events, null, 2));
  console.log(`이벤트 ${events.length}개 기록`);
}

writeFileSync(join(outDir, "_summary.json"), JSON.stringify({ base, at: new Date().toISOString(), summary }, null, 2));
console.log(`\n결과: ${summary.filter((r) => r.ok).length}/${summary.length} 성공 → ${outDir}`);
