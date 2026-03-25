import http from 'k6/http';
import { check, sleep, group, fail } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'http://localhost';
const IS_CI    = __ENV.CI === 'true';

const SUCCESS_PRODUCTS = [6, 7];      
const OOS_PRODUCTS     = [18, 22]; 
const MAX_QTY_PRODUCTS = [16, 17];  

const checkoutSuccessRate  = new Rate('checkout_success_rate');
const addToCartSuccessRate = new Rate('add_to_cart_success_rate');
const checkoutDuration     = new Trend('checkout_flow_duration_ms');
const outOfStockErrors     = new Counter('out_of_stock_errors');
const maxQtyErrors         = new Counter('max_quantity_errors');
const prePlaceOrderErrors  = new Counter('nop_pre_place_order_errors');

export const options = {
  stages: IS_CI
    ? [{ duration: '1m', target: 5 }]
    : [
        { duration: '30s', target: 40 },  
        { duration: '2m',  target: 40 },  
        { duration: '30s', target: 0 },   
      ],
  thresholds: {
    http_req_duration:        ['p(95)<5000'],
    checkout_success_rate:    ['rate>0.20'], 
    add_to_cart_success_rate: ['rate>0.40'],
  },
};


function extractToken(html) {
  const m = html.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/);
  return m ? m[1] : '';
}

function extractItemId(html) {
  const m = html.match(/name="itemquantity(\d+)"/);
  return m ? m[1] : '0';
}

function buildFakeIdentity() {
  const ts = Date.now();
  return {
    firstName: 'Load',
    lastName:  'Tester',
    email:     `user_${__VU}_${__ITER}_${ts}@test.com`,
    country:   '1',
    city:      'New York',
    address:   '123 Stress Test Ave',
    zip:       '10001',
    phone:     '5551234567',
  };
}

  

function pickProductScenario() {
  const roll = Math.random();
  
  if (roll < 0.60) { 
    const id = SUCCESS_PRODUCTS[Math.floor(Math.random() * SUCCESS_PRODUCTS.length)];
    return { id: id, qty: 1, type: 'success' }; 
  } 
  else if (roll < 0.80) { 
    const id = OOS_PRODUCTS[Math.floor(Math.random() * OOS_PRODUCTS.length)];
    return { id: id, qty: 1, type: 'oos' }; 
  } 
  else { 
    const id = MAX_QTY_PRODUCTS[Math.floor(Math.random() * MAX_QTY_PRODUCTS.length)];
    return { id: id, qty: 5, type: 'max_qty' }; 
  }
}

const AJAX_HEADERS = {
  'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
  'X-Requested-With': 'XMLHttpRequest',
};

export default function () {
  let token = '';
  const identity = buildFakeIdentity();

  group('1. Browse Homepage', function () {
    const res = http.get(`${BASE_URL}/`, { tags: { name: 'Homepage' } });
    token = extractToken(res.body || '');
    check(res, { 'homepage loaded': (r) => r.status === 200 && token !== '' });
  });
  sleep(1);

  let cartSucceeded = false;
  group('2. Add to Cart', function () {
    const pick = pickProductScenario();

    const res = http.post(
      `${BASE_URL}/addproducttocart/catalog/${pick.id}/1/${pick.qty}`,
      { __RequestVerificationToken: token },
      { headers: AJAX_HEADERS, tags: { name: 'AddToCart' } }
    );

    let isSuccess = false;
    try {
      const body = JSON.parse(res.body);
      isSuccess = body.success === true;

      if (!isSuccess) {
        const msg = (body.message || '').toString().toLowerCase();
        if (msg.includes('out of stock') || msg.includes('disabled')) {
          outOfStockErrors.add(1);
        } else if (msg.includes('maximum')) {
          maxQtyErrors.add(1);
        }
      }
    } catch (e) {}

    check(res, { 'add-to-cart response OK': () => res.status === 200 });
    addToCartSuccessRate.add(isSuccess);
    cartSucceeded = isSuccess;
  });
  sleep(1);

  if (cartSucceeded) {
    group('3. Checkout Flow', function () {
      const checkoutStart = Date.now();
      
      const opcPage = http.get(`${BASE_URL}/onepagecheckout`, { tags: { name: 'OPC_Load' } });
      let opcToken = extractToken(opcPage.body || '') || token;

      const payload = (extra) => Object.assign({ __RequestVerificationToken: opcToken }, extra || {});
      const forceCheckoutFailure = Math.random() < 0.25;
      const fakeZip = identity.zip;
      const fakeEmail = identity.email;

      let rs;
      rs = http.post(`${BASE_URL}/checkout/OpcSaveBilling`, payload({
        'BillingNewAddress.FirstName': identity.firstName,
        'BillingNewAddress.LastName': identity.lastName,
        'BillingNewAddress.Email': fakeEmail,
        'BillingNewAddress.CountryId': identity.country,
        'BillingNewAddress.City': identity.city,
        'BillingNewAddress.Address1': identity.address,
        'BillingNewAddress.ZipPostalCode': fakeZip,
        'BillingNewAddress.PhoneNumber': identity.phone,
        'ShipToSameAddress': 'true',
      }), { headers: AJAX_HEADERS });

      rs = http.post(`${BASE_URL}/checkout/OpcSaveShippingMethod`, payload({
        shippingoption: 'Ground___Shipping.FixedByWeightByTotal',
      }), { headers: AJAX_HEADERS });

      rs = http.post(`${BASE_URL}/checkout/OpcSavePaymentMethod`, payload({
        paymentmethod: 'Payments.CheckMoneyOrder',
      }), { headers: AJAX_HEADERS });

      if (!forceCheckoutFailure) {
        rs = http.post(`${BASE_URL}/checkout/OpcSavePaymentInfo`, payload(), { headers: AJAX_HEADERS });
      }

      const resConfirm = http.post(`${BASE_URL}/checkout/OpcConfirmOrder`, payload(), {
        headers: AJAX_HEADERS,
        tags: { name: 'ConfirmOrder' }
      });

      let orderOk = false;
      try {
        const body = JSON.parse(resConfirm.body);
        if (body.error === 1) {
          prePlaceOrderErrors.add(1);
          orderOk = false;
        } else {
          orderOk = (body.success === true || body.success === 1 || (body.redirect && !body.error));
        }
      } catch (e) {
        prePlaceOrderErrors.add(1);
        orderOk = false;
      }

      if (orderOk) {
        checkoutSuccessRate.add(true);
        checkoutDuration.add(Date.now() - checkoutStart);
      } else {
        checkoutSuccessRate.add(false);
      }

      check(resConfirm, { 'order confirmed': () => orderOk });
    });
  }

  sleep(1);
}