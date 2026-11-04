import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';

import { TariffsRoutingModule } from './tariffs-routing.module';
import { TariffsComponent } from './tariffs.component';


@NgModule({
  declarations: [
    TariffsComponent
  ],
  imports: [
    CommonModule,
    TariffsRoutingModule
  ]
})
export class TariffsModule { }
