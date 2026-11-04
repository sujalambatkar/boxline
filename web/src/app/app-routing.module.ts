import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'bookings' },
  {
    path: 'bookings',
    loadChildren: () => import('./features/bookings/bookings.module').then(m => m.BookingsModule),
  },
  {
    path: 'tracking',
    loadChildren: () => import('./features/tracking/tracking.module').then(m => m.TrackingModule),
  },
  {
    path: 'vessels',
    loadChildren: () => import('./features/vessels/vessels.module').then(m => m.VesselsModule),
  },
  {
    path: 'tariffs',
    loadChildren: () => import('./features/tariffs/tariffs.module').then(m => m.TariffsModule),
  },
];

@NgModule({
  imports: [RouterModule.forRoot(routes)],
  exports: [RouterModule],
})
export class AppRoutingModule {}
