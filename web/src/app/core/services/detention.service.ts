import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { CalculateRequest, DetentionResult } from '../models/detention.models';

@Injectable({ providedIn: 'root' })
export class DetentionService {
  private readonly url = `${environment.apiUrl}/detention`;

  constructor(private readonly http: HttpClient) {}

  /** Prices an arbitrary period for a container type (a what-if). */
  calculate(request: CalculateRequest): Observable<DetentionResult> {
    return this.http.post<DetentionResult>(`${this.url}/calculate`, request);
  }

  /** Prices a container's current or latest detention period from its movement history. */
  forContainer(containerNumber: string, countWeekendsAsChargeable: boolean, bookingNumber = ''): Observable<DetentionResult> {
    let params = new HttpParams().set('countWeekendsAsChargeable', countWeekendsAsChargeable);
    if (bookingNumber.trim()) {
      params = params.set('bookingNumber', bookingNumber.trim());
    }
    return this.http.get<DetentionResult>(`${this.url}/containers/${encodeURIComponent(containerNumber)}`, { params });
  }
}
