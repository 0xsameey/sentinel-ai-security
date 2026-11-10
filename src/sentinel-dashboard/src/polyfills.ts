import process from "process";

Object.assign(globalThis, {
  process,
  global: globalThis,
});
