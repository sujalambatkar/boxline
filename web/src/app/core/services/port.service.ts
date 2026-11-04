import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import { Port } from '../models/booking.models';

@Injectable({ providedIn: 'root' })
export class PortService {
  private readonly url = `${environment.apiUrl}/ports`;

  constructor(private readonly http: HttpClient) {}

  list(): Observable<Port[]> {
    return this.http.get<Port[]>(this.url);
  }
}
