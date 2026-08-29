// Isolated test input. This file is never bundled or deployed.
export function intentionallyUnsafeEvaluation(userControlled) {
  return eval(userControlled);
}
