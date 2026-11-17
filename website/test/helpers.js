import assert from "node:assert/strict";

/** Deep-compares `actual` with `expected` from the Swift fixture: same keys, same strings and
 * booleans, numbers within `tol` (relative, absolute below 1). */
export function assertClose(actual, expected, path = "$", tol = 1e-7) {
  if (expected === null) return assert.equal(actual, null, path);
  if (typeof expected === "number") {
    assert.equal(typeof actual, "number", `${path}: ${actual} is not a number`);
    assert.ok(Math.abs(actual - expected) <= tol * Math.max(1, Math.abs(expected)), `${path}: ${actual} ≠ ${expected}`);
    return;
  }
  if (typeof expected !== "object") return assert.equal(actual, expected, path);
  if (Array.isArray(expected)) {
    assert.ok(Array.isArray(actual), `${path} is not an array`);
    assert.equal(actual.length, expected.length, `${path}.length`);
    expected.forEach((e, i) => assertClose(actual[i], e, `${path}[${i}]`, tol));
    return;
  }
  assert.ok(actual && typeof actual === "object", `${path} is not an object`);
  assert.deepEqual(Object.keys(actual).sort(), Object.keys(expected).sort(), `${path} keys`);
  for (const key of Object.keys(expected)) assertClose(actual[key], expected[key], `${path}.${key}`, tol);
}
