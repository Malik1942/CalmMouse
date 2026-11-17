import { test } from "node:test";
import fixture from "./fixtures/app.json" with { type: "json" };
import { surface, surfaceNormal, deskCamera } from "../src/settings/figure.js";
import { assertClose } from "./helpers.js";

const camera = (yaw) =>
  deskCamera({ yaw, frame: fixture.stage.frameSide, originX: fixture.stage.frameOriginX, originY: fixture.stage.frameOriginY });

test("the lid is where the app's model puts it", () => {
  for (const s of fixture.surface) {
    const p = surface(s.u, s.v);
    const n = surfaceNormal(s.u, s.v);
    assertClose({ x: p.x, y: p.y, z: p.z, nx: n.x, ny: n.y, nz: n.z },
      { x: s.x, y: s.y, z: s.z, nx: s.nx, ny: s.ny, nz: s.nz }, `surface(${s.u}, ${s.v})`);
  }
});

test("points land where the renders put them, at every turn and slide", () => {
  for (const q of fixture.projected) {
    assertClose(camera(q.yaw).project(surface(q.u, q.v), q.slide), { x: q.x, y: q.y }, `project ${JSON.stringify(q)}`);
  }
});

test("a disc on the lid is the same ellipse", () => {
  for (const d of fixture.discs) {
    const points = camera(0).disc(d.u, d.v, d.radius, d.slide, d.steps).map((p) => [p.x, p.y]);
    assertClose(points, d.points, `disc(${d.u}, ${d.v})`);
  }
});
