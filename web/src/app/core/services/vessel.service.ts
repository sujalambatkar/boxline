import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { Vessel } from '../models/voyage.models';

@Injectable({ providedIn: 'root' })
export class VesselService {
  private readonly url = `${environment.apiUrl}/vessels`;

  constructor(private readonly http: HttpClient) {}

  list(search = ''): Observable<Vessel[]> {
    const params = search.trim() ? new HttpParams().set('search', search.trim()) : new HttpParams();
    return this.http.get<Vessel[]>(this.url, { params });
  }

  create(vessel: { name: string; imoNumber: string }): Observable<Vessel> {
    return this.http.post<Vessel>(this.url, vessel);
  }

  update(id: number, vessel: { name: string; imoNumber: string }): Observable<Vessel> {
    return this.http.put<Vessel>(`${this.url}/${id}`, vessel);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.url}/${id}`);
  }
}
