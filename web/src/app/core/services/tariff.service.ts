import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { SaveTariffRequest, Tariff } from '../models/detention.models';

@Injectable({ providedIn: 'root' })
export class TariffService {
  private readonly url = `${environment.apiUrl}/tariffs`;

  constructor(private readonly http: HttpClient) {}

  list(containerTypeId: number | null = null): Observable<Tariff[]> {
    const params = containerTypeId === null ? new HttpParams() : new HttpParams().set('containerTypeId', containerTypeId);
    return this.http.get<Tariff[]>(this.url, { params });
  }

  create(request: SaveTariffRequest): Observable<Tariff> {
    return this.http.post<Tariff>(this.url, request);
  }

  update(id: number, request: SaveTariffRequest): Observable<Tariff> {
    return this.http.put<Tariff>(`${this.url}/${id}`, request);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.url}/${id}`);
  }
}
