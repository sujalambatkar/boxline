import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { UtilisationQuery, UtilisationReport } from '../models/report.models';

@Injectable({ providedIn: 'root' })
export class ReportService {
  constructor(private readonly http: HttpClient) {}

  /** Container utilisation by month and location, paged on the server. */
  utilisation(query: UtilisationQuery): Observable<UtilisationReport> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);

    // A month input gives yyyy-MM; the API takes any day of the month.
    if (query.fromMonth) {
      params = params.set('fromMonth', `${query.fromMonth}-01`);
    }
    if (query.toMonth) {
      params = params.set('toMonth', `${query.toMonth}-01`);
    }
    if (query.location.trim()) {
      params = params.set('location', query.location.trim().toUpperCase());
    }

    return this.http.get<UtilisationReport>(`${environment.apiUrl}/reports/utilisation`, { params });
  }
}
