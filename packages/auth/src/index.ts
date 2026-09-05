export {
  ApiError,
  apiBase,
  authedFetch,
  clockOffsetMs,
  isUnreachable,
  request,
  serverNow,
  setTokenRenewer,
  TRANSPORT_ERROR,
  type TokenRenewer,
  type ApiProblem,
  type RequestOptions,
} from './http.js';

export { getRuntimeConfig, type RuntimeConfig } from './runtimeConfig.js';
export { newTraceParent, TRACEPARENT_PATTERN } from './trace.js';

export {
  clearSession,
  loadSession,
  login,
  logout,
  me,
  refresh,
  saveSession,
  type Me,
  type Session,
} from './session.js';

export {
  adoptSession,
  currentSession,
  endSession,
  isCurrentGeneration,
  onSessionChanged,
  renewSession,
  resetCoordinator,
  sessionGeneration,
  type SessionEvent,
} from './coordinator.js';
