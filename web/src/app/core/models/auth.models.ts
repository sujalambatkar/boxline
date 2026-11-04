export type UserRole = 'Ops' | 'Shipper';

export interface User {
  id: number;
  email: string;
  role: UserRole;
  shipperCompany: string | null;
}

export interface AuthResponse {
  accessToken: string;
  /** ISO 8601 UTC timestamp. */
  expiresAt: string;
  user: User;
}
