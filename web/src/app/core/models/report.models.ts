export interface UtilisationRow {
  /** First day of the month, yyyy-MM-dd. */
  month: string;
  location: string;
  /** The port's name; null for inland depots and unknown codes. */
  portName: string | null;
  movements: number;
  containers: number;
  loaded: number;
  discharged: number;
  /** Containers as a percentage of the whole fleet, one decimal. */
  utilisationPercent: number;
}

export interface MonthlyUtilisation {
  month: string;
  movements: number;
  /** Different containers that moved anywhere that month. */
  activeContainers: number;
  utilisationPercent: number;
}

export interface UtilisationReport {
  rows: UtilisationRow[];
  totalRows: number;
  page: number;
  pageSize: number;
  fleetSize: number;
  /** Totals for every month of the filter (not just the current page), oldest first. */
  monthly: MonthlyUtilisation[];
  elapsedMilliseconds: number;
}

export interface UtilisationQuery {
  /** yyyy-MM, or empty for the server's default (the latest twelve months). */
  fromMonth: string;
  toMonth: string;
  /** A UN/LOCODE, or empty for every location. */
  location: string;
  page: number;
  pageSize: number;
}
