import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

import { roleGuard } from '../../core/guards/auth.guards';
import { BookingFormComponent } from './booking-form/booking-form.component';
import { BookingsComponent } from './bookings.component';

const routes: Routes = [
  { path: '', component: BookingsComponent },
  { path: 'new', component: BookingFormComponent, canActivate: [roleGuard('Shipper')] },
  { path: ':id', component: BookingFormComponent },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class BookingsRoutingModule {}
