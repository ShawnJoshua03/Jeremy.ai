export type Role = "trainee" | "mentor";

// The room this browser tab is in. Kept in sessionStorage so a reload rejoins
// the same room with the same SpacetimeDB identity.
export interface Joined {
  code: string;
  name: string;
  role: Role;
  // False right after create/join from the home screen (already joined).
  // True after a reload, when the room must be joined again.
  joinNeeded: boolean;
}
