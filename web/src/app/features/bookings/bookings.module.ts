import { NgModule } from '@angular/core';

import { SharedModule } from '../../shared/shared.module';
import { BookingFormComponent } from './booking-form/booking-form.component';
import { ChatPanelComponent } from './chat-panel/chat-panel.component';
import { SailingSelectComponent } from './sailing-select/sailing-select.component';
import { BookingsRoutingModule } from './bookings-routing.module';
import { BookingsComponent } from './bookings.component';

@NgModule({
  declarations: [BookingsComponent, BookingFormComponent, ChatPanelComponent, SailingSelectComponent],
  imports: [SharedModule, BookingsRoutingModule],
})
export class BookingsModule {}
