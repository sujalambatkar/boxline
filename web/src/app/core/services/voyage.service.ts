import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { PagedResponse } from '../models/container.models';
import { SaveVoyageRequest, Voyage, VoyageListQuery, VoyageStatus } from '../models/voyage.models';

@Injectable({ providedIn: 'root' })
export class VoyageService {
  private readonly url = `${environment.apiUrl}/voyages`;

  constructor(private readonly http: HttpClient) {}

  list(query: VoyageListQuery): Observable<PagedResponse<Voyage>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.search.trim()) {
      params = params.set('search', query.search.trim());
    }
    if (query.status) {
      params = params.set('status', query.status);
    }
    if (query.vesselId !== null) {
      params = params.set('vesselId', query.vesselId);
    }
    if (query.portId !== null) {
      params = params.set('portId', query.portId);
    }
    return this.http.get<PagedResponse<Voyage>>(this.url, { params });
  }

  get(id: number): Observable<Voyage> {
    return this.http.get<Voyage>(`${this.url}/${id}`);
  }

  create(request: SaveVoyageRequest): Observable<Voyage> {
    return this.http.post<Voyage>(this.url, request);
  }

  update(id: number, request: SaveVoyageRequest): Observable<Voyage> {
    return this.http.put<Voyage>(`${this.url}/${id}`, request);
  }

  changeStatus(id: number, status: VoyageStatus): Observable<Voyage> {
    return this.http.post<Voyage>(`${this.url}/${id}/status`, { status });
  }

  recordActuals(id: number, callId: number, actuals: { ata?: string; atd?: string }): Observable<Voyage> {
    return this.http.put<Voyage>(`${this.url}/${id}/calls/${callId}/actuals`, actuals);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.url}/${id}`);
  }
}
