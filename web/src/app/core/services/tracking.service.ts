import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { BookingTracking, ContainerTracking, Movement, RecordMovementRequest } from '../models/tracking.models';

@Injectable({ providedIn: 'root' })
export class TrackingService {
  constructor(private readonly http: HttpClient) {}

  container(number: string): Observable<ContainerTracking> {
    return this.http.get<ContainerTracking>(`${environment.apiUrl}/tracking/containers/${encodeURIComponent(number)}`);
  }

  booking(number: string): Observable<BookingTracking> {
    return this.http.get<BookingTracking>(`${environment.apiUrl}/tracking/bookings/${encodeURIComponent(number)}`);
  }

  /** Records the next event for a container. Ops only. */
  record(request: RecordMovementRequest): Observable<Movement> {
    return this.http.post<Movement>(`${environment.apiUrl}/movements`, request);
  }
}
