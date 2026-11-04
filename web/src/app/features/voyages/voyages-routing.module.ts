import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

import { VoyageFormComponent } from './voyage-form/voyage-form.component';
import { VoyagesComponent } from './voyages.component';

const routes: Routes = [
  { path: '', component: VoyagesComponent },
  { path: 'new', component: VoyageFormComponent },
  { path: ':id', component: VoyageFormComponent },
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class VoyagesRoutingModule {}
