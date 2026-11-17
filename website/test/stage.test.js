import { test } from "node:test";
import fixture from "./fixtures/app.json" with { type: "json" };
import { STAGE } from "../src/settings/stage.js";
import { assertClose } from "./helpers.js";

test("the stage is the app's size, with the render where the app puts it", () => {
  const { width, height, frameSide, frameOriginX, frameOriginY, cardWidth, cardHeight, cardTop } = STAGE;
  assertClose({ width, height, frameSide, frameOriginX, frameOriginY, cardWidth, cardHeight, cardTop }, fixture.stage);
});
