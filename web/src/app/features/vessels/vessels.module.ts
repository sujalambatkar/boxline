import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';

import { VesselsRoutingModule } from './vessels-routing.module';
import { VesselsComponent } from './vessels.component';


@NgModule({
  declarations: [
    VesselsComponent
  ],
  imports: [
    CommonModule,
    VesselsRoutingModule
  ]
})
export class VesselsModule { }
