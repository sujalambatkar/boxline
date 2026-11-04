import { NgModule } from '@angular/core';

import { SharedModule } from '../../shared/shared.module';
import { BookingFormComponent } from './booking-form/booking-form.component';
import { BookingsRoutingModule } from './bookings-routing.module';
import { BookingsComponent } from './bookings.component';

@NgModule({
  declarations: [BookingsComponent, BookingFormComponent],
  imports: [SharedModule, BookingsRoutingModule],
})
export class BookingsModule {}
