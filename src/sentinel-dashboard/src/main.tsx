import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { Buffer } from "buffer";
import "./index.css";
import App from "./App.tsx";
import SecurityDemo from "./components/SecurityDemo";
import {NotificationProvider} from "./components/Notifications";

Object.assign(globalThis, { Buffer });
createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <NotificationProvider>{location.pathname==="/demo"?<SecurityDemo/>:<App />}</NotificationProvider>
  </StrictMode>,
);
