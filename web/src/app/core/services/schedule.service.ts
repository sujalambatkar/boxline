import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { ScheduleOption } from '../models/voyage.models';

@Injectable({ providedIn: 'root' })
export class ScheduleService {
  constructor(private readonly http: HttpClient) {}

  /** Open sailings from the loading port to the discharge port, earliest departure first (next 90 days by default). */
  search(polId: number, podId: number, departFrom?: string, departTo?: string): Observable<ScheduleOption[]> {
    let params = new HttpParams().set('polId', polId).set('podId', podId);
    if (departFrom) {
      params = params.set('departFrom', departFrom);
    }
    if (departTo) {
      params = params.set('departTo', departTo);
    }
    return this.http.get<ScheduleOption[]>(`${environment.apiUrl}/schedules`, { params });
  }
}
